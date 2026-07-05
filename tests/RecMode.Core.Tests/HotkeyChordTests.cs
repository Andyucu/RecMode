using RecMode.Core.Input;
using Xunit;

namespace RecMode.Core.Tests;

public class HotkeyChordTests
{
    [Theory]
    [InlineData("F9", 0u, 0x78u)]
    [InlineData("F12", 0u, 0x7Bu)]
    [InlineData("Ctrl+Shift+F9", HotkeyChord.ModControl | HotkeyChord.ModShift, 0x78u)]
    [InlineData("Alt+R", HotkeyChord.ModAlt, 0x52u)]
    [InlineData("ctrl + alt + k", HotkeyChord.ModControl | HotkeyChord.ModAlt, 0x4Bu)]
    [InlineData("Win+5", HotkeyChord.ModWin, 0x35u)]
    public void TryParse_ValidChords(string text, uint mods, uint vk)
    {
        Assert.True(HotkeyChord.TryParse(text, out HotkeyChord chord));
        Assert.Equal(mods, chord.Modifiers);
        Assert.Equal(vk, chord.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]     // modifiers only, no key
    [InlineData("Ctrl+F9+A")]      // two main keys
    [InlineData("Splat")]          // unknown key
    [InlineData(null)]
    public void TryParse_Rejects(string? text)
    {
        Assert.False(HotkeyChord.TryParse(text, out _));
    }

    [Theory]
    [InlineData("F9")]
    [InlineData("Ctrl+Shift+F9")]
    [InlineData("Ctrl+Alt+Shift+Win+K")]
    public void RoundTrips(string text)
    {
        Assert.True(HotkeyChord.TryParse(text, out HotkeyChord chord));
        Assert.Equal(text, chord.ToString());
    }

    [Fact]
    public void ToString_OrdersModifiers_CtrlAltShiftWin()
    {
        var chord = new HotkeyChord(
            HotkeyChord.ModWin | HotkeyChord.ModShift | HotkeyChord.ModAlt | HotkeyChord.ModControl, 0x78);
        Assert.Equal("Ctrl+Alt+Shift+Win+F9", chord.ToString());
    }
}
