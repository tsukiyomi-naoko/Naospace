using System.Windows.Input;
using System.Windows.Interop;
using Nova.Core.Input;
using Nova.Windows.Interop;

namespace Nova.Windows.Windowing;

/// <summary>System-wide hotkey via RegisterHotKey, delivered to a WPF window's message hook.</summary>
public sealed class GlobalHotkey : IDisposable
{
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    private readonly HwndSource _source;
    private readonly int _id;
    private bool _registered;

    public event EventHandler? Pressed;

    public GlobalHotkey(HwndSource source, int id = 0x4E4F)
    {
        _source = source;
        _id = id;
        _source.AddHook(Hook);
    }

    public HotkeyGesture? Current { get; private set; }

    /// <summary>Registers (or re-registers) the hotkey. Returns false if another application already owns it.</summary>
    public bool Register(HotkeyGesture gesture)
    {
        Unregister();
        if (!Enum.TryParse<Key>(gesture.Key, ignoreCase: true, out var key)) return false;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        uint mods = MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Alt)) mods |= MOD_ALT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Control)) mods |= MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Shift)) mods |= MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(HotkeyModifiers.Windows)) mods |= MOD_WIN;
        _registered = Native.RegisterHotKey(_source.Handle, _id, mods, vk);
        Current = _registered ? gesture : null;
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        Native.UnregisterHotKey(_source.Handle, _id);
        _registered = false;
        Current = null;
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == (int)Native.WM_HOTKEY && wParam.ToInt32() == _id)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.RemoveHook(Hook);
    }
}
