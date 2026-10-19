using System.Windows.Input;
using System.Windows.Interop;
using Zagrody.Core.Input;
using static Zagrody.App.Interop.NativeMethods;

namespace Zagrody.App.Interop;

/// <summary>
/// Hidden top-level window that receives global hotkeys and the "TaskbarCreated" broadcast
/// Explorer sends after it restarts (message-only windows don't get broadcasts).
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private readonly HwndSource _source;
    private readonly uint _taskbarCreated;
    private readonly Dictionary<int, Action> _hotkeys = new();
    private int _nextId = 1;

    public MessageWindow()
    {
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        _source = new HwndSource(new HwndSourceParameters("Zagrody.MessageWindow") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    public event EventHandler? ExplorerRestarted;

    /// <returns>An id for <see cref="Unregister"/>, or null if the shortcut is invalid or taken by another app.</returns>
    public int? Register(string text, Action callback)
    {
        if (!Hotkey.TryParse(text, out var hotkey) || ToVirtualKey(hotkey.Key) is not { } vk)
        {
            return null;
        }

        var modifiers = MOD_NOREPEAT;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Alt)) modifiers |= MOD_ALT;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Ctrl)) modifiers |= MOD_CONTROL;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Shift)) modifiers |= MOD_SHIFT;
        if (hotkey.Modifiers.HasFlag(HotkeyModifiers.Win)) modifiers |= MOD_WIN;

        var id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, modifiers, vk))
        {
            return null;
        }
        _hotkeys[id] = callback;
        return id;
    }

    public void Unregister(int id)
    {
        if (_hotkeys.Remove(id))
        {
            UnregisterHotKey(_source.Handle, id);
        }
    }

    public void UnregisterAll()
    {
        foreach (var id in _hotkeys.Keys.ToList())
        {
            Unregister(id);
        }
    }

    public static bool IsValidHotkey(string text) =>
        Hotkey.TryParse(text, out var hotkey) && ToVirtualKey(hotkey.Key) is not null;

    private static uint? ToVirtualKey(string keyName)
    {
        try
        {
            if (new KeyConverter().ConvertFromInvariantString(keyName) is Key key && key != Key.None)
            {
                return (uint)KeyInterop.VirtualKeyFromKey(key);
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or FormatException)
        {
        }
        return null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _hotkeys.TryGetValue(wParam.ToInt32(), out var callback))
        {
            callback();
            handled = true;
        }
        else if (msg == _taskbarCreated)
        {
            ExplorerRestarted?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
