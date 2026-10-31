namespace Nova.Core.Input;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>Platform-neutral representation of a global hotkey such as "Ctrl+Alt+N".</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = HotkeyModifiers.Control,
        ["control"] = HotkeyModifiers.Control,
        ["alt"] = HotkeyModifiers.Alt,
        ["shift"] = HotkeyModifiers.Shift,
        ["win"] = HotkeyModifiers.Windows,
        ["windows"] = HotkeyModifiers.Windows,
    };

    private static readonly HashSet<string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "Space", "Tab", "Enter", "Escape", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
        "Up", "Down", "Left", "Right", "Oem3", "OemTilde", "Pause", "Scroll", "Apps",
    };

    public static bool TryParse(string? text, out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false; // A global hotkey without a modifier would hijack normal typing.

        var mods = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!ModifierNames.TryGetValue(parts[i], out var m)) return false;
            mods |= m;
        }
        var key = NormalizeKey(parts[^1]);
        if (key == null) return false;
        gesture = new HotkeyGesture(mods, key);
        return true;
    }

    private static string? NormalizeKey(string key)
    {
        if (ModifierNames.ContainsKey(key)) return null;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            return char.IsDigit(key[0]) ? "D" + key : key.ToUpperInvariant();
        if (key.Length == 2 && (key[0] == 'D' || key[0] == 'd') && char.IsDigit(key[1]))
            return "D" + key[1];
        if ((key[0] == 'F' || key[0] == 'f') && int.TryParse(key[1..], out var f) && f is >= 1 and <= 24)
            return "F" + f;
        var named = NamedKeys.FirstOrDefault(n => n.Equals(key, StringComparison.OrdinalIgnoreCase));
        return named;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(Key.Length == 2 && Key[0] == 'D' && char.IsDigit(Key[1]) ? Key[1..] : Key);
        return string.Join("+", parts);
    }
}
