using static Zagrody.App.Interop.NativeMethods;

namespace Zagrody.App.Interop;

/// <summary>Finds Explorer's desktop icon view and toggles it the same way "View → Show desktop icons" does.</summary>
internal static class DesktopIcons
{
    private const int ToggleDesktopIconsCommand = 0x7402;

    public static bool IsVisible
    {
        get
        {
            var listView = FindListView();
            return listView == IntPtr.Zero || IsWindowVisible(listView);
        }
    }

    public static void SetVisible(bool visible)
    {
        if (IsVisible != visible && FindDefView() is var defView && defView != IntPtr.Zero)
        {
            SendMessage(defView, WM_COMMAND, new IntPtr(ToggleDesktopIconsCommand), IntPtr.Zero);
        }
    }

    /// <summary>The SysListView32 that draws desktop icons.</summary>
    public static IntPtr FindListView()
    {
        var defView = FindDefView();
        return defView == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
    }

    public static bool IsDesktopListView(IntPtr hwnd) =>
        hwnd != IntPtr.Zero
        && GetClassName(hwnd) == "SysListView32"
        && GetClassName(GetParent(hwnd)) == "SHELLDLL_DefView";

    private static IntPtr FindDefView()
    {
        // Usually under Progman, but after the wallpaper slideshow or Win+Tab it moves under a WorkerW.
        var progman = FindWindow("Progman", null);
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
        if (defView != IntPtr.Zero)
        {
            return defView;
        }

        EnumWindows((hwnd, _) =>
        {
            if (GetClassName(hwnd) == "WorkerW")
            {
                var child = FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (child != IntPtr.Zero)
                {
                    defView = child;
                    return false;
                }
            }
            return true;
        }, IntPtr.Zero);
        return defView;
    }
}
