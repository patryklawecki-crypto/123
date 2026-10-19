using System.IO;
using System.Windows.Media;
using System.Windows.Threading;
using Zagrody.App.Interop;
using Zagrody.Core.Search;

namespace Zagrody.App.ViewModels;

/// <summary>One file, folder or shortcut shown in a fence.</summary>
public sealed class ItemViewModel : ObservableObject, ISortableItem
{
    private static readonly HashSet<string> HiddenExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".lnk", ".url", ".appref-ms",
    };

    private readonly int _iconSize;
    private ImageSource? _icon;
    private bool _iconRequested;

    private ItemViewModel(string path, bool isDirectory, bool isMissing, long size, DateTime lastWriteUtc, int iconSize)
    {
        Path = path;
        IsDirectory = isDirectory;
        IsMissing = isMissing;
        Size = size;
        LastWriteTimeUtc = lastWriteUtc;
        _iconSize = iconSize;

        var name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name))
        {
            name = path; // drive roots like "D:\"
        }
        Extension = isDirectory ? string.Empty : System.IO.Path.GetExtension(path).ToLowerInvariant();
        DisplayName = HiddenExtensions.Contains(Extension) ? System.IO.Path.GetFileNameWithoutExtension(name) : name;
    }

    public string Path { get; }
    public string DisplayName { get; }
    public string Extension { get; }
    public bool IsDirectory { get; }
    public bool IsMissing { get; }
    public long Size { get; }
    public DateTime LastWriteTimeUtc { get; }

    /// <summary>Loaded on first use at background priority, so a fence with many items still appears instantly.</summary>
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    _icon = ShellIcons.Get(Path, IsDirectory, _iconSize);
                    OnPropertyChanged(nameof(Icon));
                });
            }
            return _icon;
        }
    }

    public string ToolTip
    {
        get
        {
            if (IsMissing)
            {
                return $"{Path}\nNie znaleziono — element został przeniesiony lub usunięty.";
            }
            var details = IsDirectory ? "Folder" : FormatSize(Size);
            return $"{Path}\n{details} · zmieniono {LastWriteTimeUtc.ToLocalTime():g}";
        }
    }

    public static ItemViewModel FromInfo(FileSystemInfo info, int iconSize) => info switch
    {
        FileInfo f => new ItemViewModel(f.FullName, false, false, f.Length, f.LastWriteTimeUtc, iconSize),
        _ => new ItemViewModel(info.FullName, true, false, 0, info.LastWriteTimeUtc, iconSize),
    };

    public static ItemViewModel FromPath(string path, int iconSize)
    {
        try
        {
            if (File.Exists(path))
            {
                return FromInfo(new FileInfo(path), iconSize);
            }
            if (Directory.Exists(path))
            {
                return FromInfo(new DirectoryInfo(path), iconSize);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return new ItemViewModel(path, !System.IO.Path.HasExtension(path), true, 0, DateTime.MinValue, iconSize);
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }
}
