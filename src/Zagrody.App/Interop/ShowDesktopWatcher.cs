using static Zagrody.App.Interop.NativeMethods;

namespace Zagrody.App.Interop;

/// <summary>
/// Notices "Show desktop" (Win+D, the corner of the taskbar) through foreground-change events: the
/// desktop becomes the foreground window and no application window is left in front of it. A plain
/// click on the wallpaper doesn't count, because the application windows stay above the desktop then.
/// </summary>
internal sealed class ShowDesktopWatcher : IDisposable
{
    private readonly WinEventDelegate _proc;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private IntPtr _hook;
    private bool _desktopShown;

    public ShowDesktopWatcher()
    {
        _proc = OnForegroundChanged; // must stay referenced for as long as the hook is installed
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>True while the desktop is in front; false once an application is back.</summary>
    public event EventHandler<bool>? DesktopShownChanged;

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        var root = GetAncestor(hwnd, GA_ROOT);
        var shown = IsDesktopWindow(root) && !AnyApplicationWindowAbove(root);
        if (shown != _desktopShown)
        {
            _desktopShown = shown;
            DesktopShownChanged?.Invoke(this, shown);
        }
    }

    private static bool IsDesktopWindow(IntPtr hwnd) =>
        GetClassName(hwnd) is "Progman" or "WorkerW";

    private bool AnyApplicationWindowAbove(IntPtr desktop)
    {
        for (var w = GetWindow(desktop, GW_HWNDPREV); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDPREV))
        {
            if (IsApplicationWindow(w))
            {
                return true;
            }
        }
        return false;
    }

    private bool IsApplicationWindow(IntPtr hwnd)
    {
        if (!IsWindowVisible(hwnd) || IsIconic(hwnd) || IsCloaked(hwnd))
        {
            return false;
        }
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == _ownProcessId)
        {
            return false;
        }
        var exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        if ((exStyle & (WS_EX_TOPMOST | WS_EX_TOOLWINDOW)) != 0)
        {
            return false; // taskbar, overlays, tooltips, other widgets
        }
        if (!GetWindowRect(hwnd, out var rect) || rect.Right - rect.Left < 50 || rect.Bottom - rect.Top < 50)
        {
            return false;
        }
        return GetClassName(hwnd) is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        try
        {
            return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out var cloaked, sizeof(int)) == 0 && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
