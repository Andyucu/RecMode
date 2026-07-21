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

    // Compact has no caption chrome to close it, but Alt+F4 (or a taskbar close, since ShowInTaskbar=True)
    // still reaches WM_SYSCOMMAND directly. The app runs under ShutdownMode.OnExplicitShutdown, so without
    // this override that would just close this window and leave the process running headless in the tray —
    // same fix as ShellWindow.OnClosing (shares its AppShutdownState gate to avoid re-entrant Shutdown() calls
    // when ShellPresenter is keeping the other layout's window alive-but-hidden).
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AppShutdownState.InProgress)
        {
            e.Cancel = true;
            AppShutdownState.InProgress = true;
            Application.Current.Shutdown();
            return;
        }

        base.OnClosing(e);
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
