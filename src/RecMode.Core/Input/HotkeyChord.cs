using System.Text;

namespace RecMode.Core.Input;

/// <summary>
/// A global-hotkey chord — modifier mask + virtual-key — with round-trippable text ("Ctrl+Shift+F9"), used by
/// the remappable-hotkeys UI (plan Phase 9). The modifier values match Win32 <c>RegisterHotKey</c>'s
/// <c>MOD_*</c> flags, so <see cref="Modifiers"/> can be passed to it directly. Pure/no interop → unit-testable.
/// </summary>
public sealed record HotkeyChord(uint Modifiers, uint VirtualKey)
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    /// <summary>Parses "Ctrl+Shift+F9" (case/space-insensitive). Returns false if there's no valid main key.</summary>
    public static bool TryParse(string? text, out HotkeyChord chord)
    {
        chord = new HotkeyChord(0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint mods = 0;
        uint vk = 0;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= ModControl; break;
                case "alt": mods |= ModAlt; break;
                case "shift": mods |= ModShift; break;
                case "win" or "windows": mods |= ModWin; break;
                default:
                    if (vk != 0 || !TryKeyToVk(raw, out vk))
                    {
                        return false; // two main keys, or an unknown token
                    }
                    break;
            }
        }

        if (vk == 0)
        {
            return false;
        }

        chord = new HotkeyChord(mods, vk);
        return true;
    }

    public override string ToString()
    {
        var sb = new StringBuilder();
        if ((Modifiers & ModControl) != 0) sb.Append("Ctrl+");
        if ((Modifiers & ModAlt) != 0) sb.Append("Alt+");
        if ((Modifiers & ModShift) != 0) sb.Append("Shift+");
        if ((Modifiers & ModWin) != 0) sb.Append("Win+");
        sb.Append(VkToKey(VirtualKey));
        return sb.ToString();
    }

    private static bool TryKeyToVk(string key, out uint vk)
    {
        vk = 0;
        key = key.ToUpperInvariant();

        if (NamedKeys.TryGetValue(key, out vk)) return true;
        if (key.StartsWith("0X", StringComparison.Ordinal) && uint.TryParse(key.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out vk) && vk is > 0 and <= 0xFE)
            return true;

        // Function keys F1–F24 → 0x70–0x87.
        if (key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }

        // A–Z → 0x41–0x5A, 0–9 → 0x30–0x39.
        if (key.Length == 1)
        {
            char c = key[0];
            if (c is >= 'A' and <= 'Z') { vk = c; return true; }
            if (c is >= '0' and <= '9') { vk = c; return true; }
        }

        return false;
    }

    private static string VkToKey(uint vk)
    {
        if (vk is >= 0x70 and <= 0x87)
        {
            return "F" + (vk - 0x70 + 1);
        }

        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39)
        {
            return ((char)vk).ToString();
        }

        return NamedKeys.FirstOrDefault(pair => pair.Value == vk).Key ?? $"0x{vk:X2}";
    }

    private static readonly IReadOnlyDictionary<string, uint> NamedKeys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Space"] = 0x20, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["End"] = 0x23, ["Home"] = 0x24, ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27,
        ["Down"] = 0x28, ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Tab"] = 0x09,
        ["Backspace"] = 0x08, ["Escape"] = 0x1B,
    };
}
