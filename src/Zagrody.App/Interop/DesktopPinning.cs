using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using static Zagrody.App.Interop.NativeMethods;

namespace Zagrody.App.Interop;

/// <summary>
/// Makes a window behave like part of the desktop: hidden from Alt+Tab and the taskbar,
/// kept below normal windows, and surviving Win+D. Being owned by Progman (the desktop window)
/// is what keeps "Show desktop" from minimizing it.
/// </summary>
internal static class DesktopPinning
{
    public static void Pin(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();

        var exStyle = (long)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));

        Repin(window);

        var source = HwndSource.FromHwnd(hwnd);
        source.RemoveHook(KeepAtBottom);
        source.AddHook(KeepAtBottom);
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
        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private static IntPtr KeepAtBottom(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SWP_NOZORDER) == 0)
            {
                pos.hwndInsertAfter = HWND_BOTTOM;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        return IntPtr.Zero;
    }
}
