using System.Runtime.InteropServices;
using System.Windows.Threading;
using static Zagrody.App.Interop.NativeMethods;

namespace Zagrody.App.Interop;

/// <summary>
/// Detects a double-click on an empty spot of the desktop through a low-level mouse hook.
/// "Empty" is checked after the click settles: Explorer clears the selection when the click
/// lands between icons, and selects an icon when it lands on one.
/// </summary>
internal sealed class DesktopDoubleClickWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly LowLevelMouseProc _proc;
    private IntPtr _hook;
    private uint _lastClickTime;
    private POINT _lastClickPoint;

    public DesktopDoubleClickWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _proc = HookProc; // must stay referenced for as long as the hook is installed
    }

    public event EventHandler? DoubleClicked;

    public bool IsEnabled
    {
        get => _hook != IntPtr.Zero;
        set
        {
            if (value && _hook == IntPtr.Zero)
            {
                _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
            }
            else if (!value && _hook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == WM_LBUTTONDOWN)
        {
            // Keep this path short: Windows drops hooks that are slow to return.
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var isDouble =
                data.time - _lastClickTime <= GetDoubleClickTime()
                && Math.Abs(data.pt.X - _lastClickPoint.X) <= GetSystemMetrics(SM_CXDOUBLECLK)
                && Math.Abs(data.pt.Y - _lastClickPoint.Y) <= GetSystemMetrics(SM_CYDOUBLECLK);

            _lastClickTime = isDouble ? 0 : data.time;
            _lastClickPoint = data.pt;

            if (isDouble)
            {
                var point = data.pt;
                _dispatcher.BeginInvoke(DispatcherPriority.Background, () => CheckDesktop(point));
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private async void CheckDesktop(POINT point)
    {
        var target = WindowFromPoint(point);
        if (!DesktopIcons.IsDesktopListView(target))
        {
            // With desktop icons hidden there is no list view, the click lands on the desktop itself.
            if (GetClassName(target) is "SHELLDLL_DefView" or "WorkerW" or "Progman")
            {
                DoubleClicked?.Invoke(this, EventArgs.Empty);
            }
            return;
        }

        await Task.Delay(80); // let Explorer update its selection
        if (SendMessage(target, LVM_GETSELECTEDCOUNT, IntPtr.Zero, IntPtr.Zero) == IntPtr.Zero)
        {
            DoubleClicked?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose() => IsEnabled = false;
}
