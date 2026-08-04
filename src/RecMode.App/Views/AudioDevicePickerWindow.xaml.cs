using System.Windows;
using System.Windows.Input;
using RecMode.App.ViewModels;

namespace RecMode.App.Views;

/// <summary>Modal "System audio devices" picker — see <see cref="AudioDevicePickerViewModel"/>.</summary>
public partial class AudioDevicePickerWindow : Window
{
    public AudioDevicePickerWindow(AudioDevicePickerViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
