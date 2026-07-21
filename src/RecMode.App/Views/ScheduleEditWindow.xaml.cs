using System.Windows;
using System.Windows.Input;
using RecMode.App.ViewModels;

namespace RecMode.App.Views;

/// <summary>Modal editor for a single schedule (name, recurrence, start time, duration). Phase 6.</summary>
public partial class ScheduleEditWindow : Window
{
    private readonly ScheduleEditViewModel _model;

    public ScheduleEditWindow(ScheduleEditViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;

        TitleBar.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); } };
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
            MessageBox.Show(this, RecMode.App.Resources.Strings.ScheduleEdit_InvalidTime,
                RecMode.App.Resources.Strings.ScheduleEdit_Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
        Close();
    }
}
