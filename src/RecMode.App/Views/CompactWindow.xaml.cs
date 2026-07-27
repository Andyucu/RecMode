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
        IsVisibleChanged += OnIsVisibleChanged;
    }

    // Same §3.9 wiring as ShellWindow.OnIsVisibleChanged — both windows share one RecordViewModel instance
    // (ShellPresenter swaps which is Current, but the underlying page/viewmodel is the same either way).
    // hostsPreviewSurfaces is false here specifically: CompactWindow.xaml binds none of PreviewImage/
    // HasPreview/SystemMeter/MicMeter (only source tiles and audio enable toggles) — starting a full WGC/
    // D3D11 preview plus live WASAPI metering while this window is the one shown would burn §3.9's exact
    // budget for a surface nothing here displays. See RecordViewModel.SetWindowVisible's doc comment.
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is ShellViewModel shell)
        {
            if ((bool)e.NewValue)
            {
                shell.EnsureInitialPageLoaded();
            }

            shell.Record.SetWindowVisible((bool)e.NewValue, hostsPreviewSurfaces: false);
        }
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
            // Same Settings → General → "Close button minimizes to tray" behavior as ShellWindow.OnClosing —
            // reachable here via Alt+F4/taskbar close even though Compact has no close button of its own.
            if (DataContext is ShellViewModel { Settings.CloseToTray: true })
            {
                e.Cancel = true;
                Hide();
                return;
            }

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
