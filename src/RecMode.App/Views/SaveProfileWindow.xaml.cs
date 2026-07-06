using System.Windows;
using System.Windows.Input;
using RecMode.App.ViewModels;

namespace RecMode.App.Views;

/// <summary>Modal name prompt for saving the current Record settings as a custom profile.</summary>
public partial class SaveProfileWindow : Window
{
    private readonly SaveProfileViewModel _model;

    public SaveProfileWindow(SaveProfileViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;

        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!_model.IsValid)
        {
            MessageBox.Show(this, RecMode.App.Resources.Strings.Profile_InvalidName,
                RecMode.App.Resources.Strings.Profile_SaveTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
