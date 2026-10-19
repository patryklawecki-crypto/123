using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static Zagrody.App.Interop.NativeMethods;

namespace Zagrody.App.Interop;

/// <summary>
/// Makes a window behave like part of the desktop: hidden from Alt+Tab and the taskbar, kept below
/// normal windows, and surviving Win+D. How "Show desktop" treats such windows differs between Windows
/// builds, so several independent defences are layered:
/// <list type="number">
/// <item>Owned by Progman, so the window always stays above the desktop itself.</item>
/// <item>No minimize box and SC_MINIMIZE is refused, so "minimize all" has nothing to minimize.</item>
/// <item>If the shell still minimizes or hides it, it is restored right away without taking focus.</item>
/// <item>While the desktop is in front (Win+D), <see cref="SetRaised"/> lifts fences above it.</item>
/// </list>
/// </summary>
internal static class DesktopPinning
{
    private static readonly HashSet<IntPtr> IntentionallyHidden = new();
    private static bool _raised;

    public static void Pin(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

        var style = (long)GetWindowLongPtr(hwnd, GWL_STYLE);
        style &= ~(WS_MINIMIZEBOX | WS_MAXIMIZEBOX);
        SetWindowLongPtr(hwnd, GWL_STYLE, new IntPtr(style));

        Repin(window);

        var source = HwndSource.FromHwnd(hwnd);
        source.RemoveHook(WndProc);
        source.AddHook(WndProc);
    }

    /// <summary>Re-attaches to the desktop; needed after Explorer restarts and recreates Progman.</summary>
    public static void Repin(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }
        var progman = FindWindow("Progman", null);
        if (progman != IntPtr.Zero)
        {
            SetWindowLongPtr(hwnd, GWLP_HWNDPARENT, progman);
        }
        SetWindowPos(hwnd, _raised ? HWND_TOP : HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Hides a window on purpose; plain hides coming from the shell are undone.</summary>
    public static void Hide(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        IntentionallyHidden.Add(hwnd);
        window.Hide();
    }

    public static void Show(Window window)
    {
        IntentionallyHidden.Remove(new WindowInteropHelper(window).Handle);
        window.Show();
        Repin(window);
    }

    /// <summary>Called before closing, so the hide that is part of destroying a window goes through.</summary>
    public static void Release(Window window) => IntentionallyHidden.Add(new WindowInteropHelper(window).Handle);

    public static bool IsRaised => _raised;

    /// <summary>Brings back windows that the shell minimized or hid although they should be showing.</summary>
    public static void RestoreIfLost(IEnumerable<Window> windows)
    {
        foreach (var window in windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !window.IsVisible || IntentionallyHidden.Contains(hwnd))
            {
                continue;
            }
            if (IsIconic(hwnd) || !IsWindowVisible(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                SetWindowPos(hwnd, _raised ? HWND_TOP : HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    /// <summary>True when any visible window lies under <paramref name="cover"/> in the z-order.</summary>
    public static bool AnyBelow(IEnumerable<Window> windows, IntPtr cover)
    {
        foreach (var window in windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !window.IsVisible)
            {
                continue;
            }
            for (var w = GetWindow(hwnd, GW_HWNDPREV); w != IntPtr.Zero; w = GetWindow(w, GW_HWNDPREV))
            {
                if (w == cover)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Lifts fences above everything while the desktop is in front ("Show desktop"), and puts them back
    /// to the bottom once the user switches to an application again.
    /// </summary>
    public static void SetRaised(IEnumerable<Window> windows, bool raised)
    {
        _raised = raised;
        foreach (var window in windows)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !window.IsVisible)
            {
                continue;
            }
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            }
            if (raised)
            {
                // Through TOPMOST and back: the reliable way to reach the top of the z-order without activating.
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
            else
            {
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_WINDOWPOSCHANGING:
            {
                var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                var changed = false;
                if ((pos.flags & SWP_NOZORDER) == 0 && !_raised)
                {
                    pos.hwndInsertAfter = HWND_BOTTOM;
                    changed = true;
                }
                if ((pos.flags & SWP_HIDEWINDOW) != 0 && !IntentionallyHidden.Contains(hwnd))
                {
                    pos.flags &= ~SWP_HIDEWINDOW;
                    changed = true;
                }
                if (changed)
                {
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
            }

            case WM_SYSCOMMAND when (wParam.ToInt64() & 0xFFF0) == SC_MINIMIZE:
                handled = true;
                break;

            case WM_SIZE when wParam.ToInt64() == SIZE_MINIMIZED:
                // Minimized anyway (e.g. by "Show desktop"): come straight back without stealing focus.
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Send, () =>
                {
                    if (!IntentionallyHidden.Contains(hwnd))
                    {
                        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
                        SetWindowPos(hwnd, _raised ? HWND_TOP : HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                });
                break;
        }
        return IntPtr.Zero;
    }
}
