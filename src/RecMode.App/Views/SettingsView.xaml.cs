using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RecMode.App.ViewModels;
using RecMode.Core.Input;

namespace RecMode.App.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnChangeHotkey(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && sender is Button { Tag: string action })
        {
            vm.ChangeHotkeyCommand.Execute(action);
            Keyboard.Focus(this); // route the next key press to OnCaptureKeyDown
        }
    }

    private void OnCaptureKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || !vm.IsCapturingHotkey)
        {
            return;
        }

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        // Ignore a modifier pressed on its own — wait for the actual key.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return;
        }

        e.Handled = true;

        if (key == Key.Escape)
        {
            vm.CancelHotkeyCommand.Execute(null);
            return;
        }

        uint mods = 0;
        ModifierKeys m = Keyboard.Modifiers;
        if (m.HasFlag(ModifierKeys.Control)) mods |= HotkeyChord.ModControl;
        if (m.HasFlag(ModifierKeys.Alt)) mods |= HotkeyChord.ModAlt;
        if (m.HasFlag(ModifierKeys.Shift)) mods |= HotkeyChord.ModShift;
        if (m.HasFlag(ModifierKeys.Windows)) mods |= HotkeyChord.ModWin;

        var chord = new HotkeyChord(mods, (uint)KeyInterop.VirtualKeyFromKey(key));
        vm.CompleteCapture(chord.ToString());
    }
}
