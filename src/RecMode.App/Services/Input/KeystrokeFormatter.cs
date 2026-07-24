namespace RecMode.App.Services;

/// <summary>
/// Pure formatting logic for the keystroke visualizer: turns a raw vk code + modifier state from
/// <see cref="GlobalKeyboardHook"/> into a display string like "Ctrl + Z", or <c>null</c> when the key press
/// shouldn't be shown. Deliberately scoped to hotkey *combinations*, not general typing: a bare letter/digit
/// (typing prose during a tutorial) returns null, but a modifier combo or a standalone "special" key (Esc,
/// Tab, F-keys, arrows, Delete, ...) is always shown, since those are the presses viewers actually need to see.
/// </summary>
public static class KeystrokeFormatter
{
    private static readonly HashSet<uint> ModifierVks = [0x10, 0xA0, 0xA1, 0x11, 0xA2, 0xA3, 0x12, 0xA4, 0xA5, 0x5B, 0x5C];

    public static string? Format(uint vk, bool ctrl, bool alt, bool shift, bool win)
    {
        if (ModifierVks.Contains(vk))
        {
            return null; // a modifier held alone isn't a combo yet
        }

        string? key = KeyName(vk);
        if (key is null)
        {
            return null;
        }

        bool hasModifier = ctrl || alt || win;
        if (!hasModifier && !IsStandaloneKey(vk))
        {
            return null; // plain typing (including shifted letters), not a hotkey
        }

        List<string> parts = [];
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(key);
        return string.Join(" + ", parts);
    }

    private static bool IsStandaloneKey(uint vk) => vk switch
    {
        0x1B or 0x09 or 0x0D or 0x2E or 0x08 or 0x2C or 0x2D or 0x24 or 0x23 or 0x21 or 0x22 => true,
        >= 0x25 and <= 0x28 => true, // arrows
        >= 0x70 and <= 0x87 => true, // F1-F24
        _ => false,
    };

    private static string? KeyName(uint vk) => vk switch
    {
        >= 0x30 and <= 0x39 => ((char)vk).ToString(), // 0-9
        >= 0x41 and <= 0x5A => ((char)vk).ToString(), // A-Z
        >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",   // F1-F24
        0x1B => "Esc",
        0x09 => "Tab",
        0x0D => "Enter",
        0x20 => "Space",
        0x08 => "Backspace",
        0x2E => "Delete",
        0x2D => "Insert",
        0x24 => "Home",
        0x23 => "End",
        0x21 => "Page Up",
        0x22 => "Page Down",
        0x25 => "←",
        0x26 => "↑",
        0x27 => "→",
        0x28 => "↓",
        0x2C => "Print Screen",
        0xBA => ";",
        0xBB => "=",
        0xBC => ",",
        0xBD => "-",
        0xBE => ".",
        0xBF => "/",
        0xC0 => "`",
        0xDB => "[",
        0xDC => "\\",
        0xDD => "]",
        0xDE => "'",
        _ => null,
    };
}
