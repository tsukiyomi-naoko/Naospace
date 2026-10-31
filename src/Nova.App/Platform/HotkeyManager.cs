using System.Windows.Input;
using System.Windows.Interop;

namespace Nova.App.Platform;

/// <summary>System-wide hotkeys via RegisterHotKey on the main window's HWND.</summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 0x4E00;

    public HotkeyManager(HwndSource source)
    {
        _source = source;
        _source.AddHook(WndProc);
    }

    /// <summary>Registers e.g. "Ctrl+Alt+Space". Returns null on success, else a reason.</summary>
    public string? Register(string gesture, Action handler)
    {
        if (!TryParse(gesture, out var mods, out var vk)) return $"\"{gesture}\" is not a valid hotkey.";
        var id = _nextId++;
        if (!Native.RegisterHotKey(_source.Handle, id, mods | Native.MOD_NOREPEAT, vk))
            return $"{gesture} is already in use by another application.";
        _handlers[id] = handler;
        return null;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys) Native.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
    }

    public static bool TryParse(string gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(gesture)) return false;
        var parts = gesture.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;
        foreach (var p in parts[..^1])
        {
            switch (p.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= Native.MOD_CONTROL; break;
                case "alt": modifiers |= Native.MOD_ALT; break;
                case "shift": modifiers |= Native.MOD_SHIFT; break;
                case "win": case "windows": modifiers |= Native.MOD_WIN; break;
                default: return false;
            }
        }
        var keyText = parts[^1];
        if (keyText.Length == 1 && char.IsDigit(keyText[0])) keyText = "D" + keyText;
        if (!Enum.TryParse<Key>(keyText, ignoreCase: true, out var key) || key == Key.None) return false;
        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        return vk != 0 && modifiers != 0;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var h))
        {
            handled = true;
            h();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
    }
}
