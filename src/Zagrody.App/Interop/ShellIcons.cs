using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Zagrody.App.Interop;

/// <summary>
/// Loads the same icons Explorer shows, at up to 256 px, through the system image lists.
/// Icons that depend only on the extension are cached per extension; executables, shortcuts
/// and folders can have their own icon and are cached per path.
/// </summary>
internal static class ShellIcons
{
    private const uint SHGFI_SYSICONINDEX = 0x4000;
    private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
    private const int ILD_TRANSPARENT = 0x1;
    private const int SHIL_LARGE = 0;      // 32 px
    private const int SHIL_SMALL = 1;      // 16 px
    private const int SHIL_EXTRALARGE = 2; // 48 px
    private const int SHIL_JUMBO = 4;      // 256 px

    private static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    private static readonly HashSet<string> PerFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".lnk", ".ico", ".url", ".appref-ms", ".msc", ".cpl", ".scr",
    };

    private static readonly ConcurrentDictionary<int, IImageList?> ImageLists = new();
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        // Only GetIcon is used; the slots before it must exist to keep the vtable layout.
        [PreserveSig] int Add(IntPtr image, IntPtr mask, ref int index);
        [PreserveSig] int ReplaceIcon(int i, IntPtr icon, ref int index);
        [PreserveSig] int SetOverlayImage(int image, int overlay);
        [PreserveSig] int Replace(int i, IntPtr image, IntPtr mask);
        [PreserveSig] int AddMasked(IntPtr image, int mask, ref int index);
        [PreserveSig] int Draw(IntPtr drawParams);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr icon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int list, ref Guid riid, out IImageList imageList);

    public static ImageSource? Get(string path, bool isDirectory, int size)
    {
        var list = size switch
        {
            <= 16 => SHIL_SMALL,
            <= 32 => SHIL_LARGE,
            <= 48 => SHIL_EXTRALARGE,
            _ => SHIL_JUMBO,
        };

        var extension = Path.GetExtension(path);
        var exists = isDirectory ? Directory.Exists(path) : File.Exists(path);
        var perFile = isDirectory || PerFileExtensions.Contains(extension);
        var key = $"{list}|{(perFile && exists ? path : (isDirectory ? "<dir>" : extension))}";

        return Cache.GetOrAdd(key, _ => Load(path, isDirectory, exists, list));
    }

    public static void ClearCache() => Cache.Clear();

    private static ImageSource? Load(string path, bool isDirectory, bool exists, int list)
    {
        var info = new SHFILEINFO();
        var flags = SHGFI_SYSICONINDEX;
        uint attributes = 0;
        if (!exists)
        {
            // Lets the shell answer for files that are gone (dimmed "missing" items) by extension alone.
            flags |= SHGFI_USEFILEATTRIBUTES;
            attributes = isDirectory ? 0x10u : FILE_ATTRIBUTE_NORMAL;
        }

        if (SHGetFileInfo(path, attributes, ref info, (uint)Marshal.SizeOf(info), flags) == IntPtr.Zero)
        {
            return null;
        }

        var icon = ExtractIcon(list, info.iIcon);

        // Files without a 256 px icon come back from the jumbo list as a 48 px image in the top-left
        // corner of an empty canvas; the 48 px list scales up better than that.
        if (list == SHIL_JUMBO && icon is BitmapSource jumbo && IsOnlyTopLeftCorner(jumbo, 48))
        {
            icon = ExtractIcon(SHIL_EXTRALARGE, info.iIcon) ?? icon;
        }
        return icon;
    }

    private static ImageSource? ExtractIcon(int list, int index)
    {
        if (GetImageList(list) is not { } imageList)
        {
            return null;
        }

        var hIcon = IntPtr.Zero;
        try
        {
            if (imageList.GetIcon(index, ILD_TRANSPARENT, ref hIcon) != 0 || hIcon == IntPtr.Zero)
            {
                return null;
            }
            var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            if (hIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }
    }

    private static bool IsOnlyTopLeftCorner(BitmapSource bitmap, int corner)
    {
        if (bitmap.PixelWidth <= corner || bitmap.PixelHeight <= corner)
        {
            return false;
        }
        var converted = bitmap.Format == PixelFormats.Bgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (var y = 0; y < converted.PixelHeight; y++)
        {
            for (var x = y < corner ? corner : 0; x < converted.PixelWidth; x++)
            {
                if (pixels[y * stride + x * 4 + 3] != 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    // The system image lists live for the whole process, so they are fetched once and never released.
    private static IImageList? GetImageList(int list) =>
        ImageLists.GetOrAdd(list, l =>
        {
            var iid = IID_IImageList;
            return SHGetImageList(l, ref iid, out var imageList) == 0 ? imageList : null;
        });
}
