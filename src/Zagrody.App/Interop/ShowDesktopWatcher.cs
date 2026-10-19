using static Zagrody.App.Interop.NativeMethods;

namespace Zagrody.App.Interop;

/// <param name="Shown">The desktop is in front and no application window covers it.</param>
/// <param name="Desktop">The desktop's top-level window while <paramref name="Shown"/>.</param>
internal readonly record struct DesktopState(bool Shown, IntPtr Desktop);

/// <summary>
/// Tells whether "Show desktop" (Win+D, the corner of the taskbar) is active: the desktop is the
/// foreground window and no application window is left in front of it. A plain click on the
/// wallpaper doesn't count, because the application windows stay above the desktop then.
/// Foreground changes are reported as they happen; callers also poll <see cref="Evaluate"/>, since
/// rapid Win+D presses can coalesce events and windows are still animating when they arrive.
/// </summary>
internal sealed class ShowDesktopWatcher : IDisposable
{
    private readonly WinEventDelegate _proc;
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;
    private IntPtr _hook;

    public ShowDesktopWatcher()
    {
        _proc = (_, _, _, _, _, _, _) => ForegroundChanged?.Invoke(this, EventArgs.Empty); // kept referenced while hooked
        _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
    }

    public event EventHandler? ForegroundChanged;

    /// <returns>The current state, or null while one of our own windows is in front (keep whatever was set).</returns>
    public DesktopState? Evaluate()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return null;
        }
        GetWindowThreadProcessId(foreground, out var pid);
        if (pid == _ownProcessId)
        {
            return null;
        }
        var root = GetAncestor(foreground, GA_ROOT);
        return IsDesktopWindow(root) && !AnyApplicationWindowAbove(root)
            ? new DesktopState(true, root)
            : new DesktopState(false, IntPtr.Zero);
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
