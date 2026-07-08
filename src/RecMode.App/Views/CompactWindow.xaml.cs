using System.Windows;
using System.Windows.Input;
using RecMode.App.ViewModels;

namespace RecMode.App.Views;

/// <summary>
/// The compact launcher (plan §1 "compact launcher"/Phase 6): a small always-on-top widget alternative to the
/// full <see cref="ShellWindow"/> — source tiles, quick system/mic audio, Record/Screenshot, elapsed time.
/// Has no title bar (dragged via the header row) and no minimize/maximize/close chrome; the user leaves
/// Compact layout via "Expand to full window" or quits via the tray icon, matching how <c>--tray</c> mode
/// already has no visible window to close either.
/// </summary>
public partial class CompactWindow : Window
{
    public CompactWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Top-right of the primary work area (DIP units), a sensible default for an always-on-top launcher.
        Rect work = SystemParameters.WorkArea;
        Left = work.Right - ActualWidth - 24;
        Top = work.Top + 24;
    }

    private void OnHeaderDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
