using System.Windows;
using System.Windows.Interop;
using RecMode.App.Themes;
using RecMode.App.ViewModels;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Views;

/// <summary>
/// Shared by every top-level main-window candidate (<see cref="ShellWindow"/>, <see cref="CompactWindow"/>) so
/// their <c>OnClosing</c> overrides gate on one flag instead of each their own — <see
/// cref="Application.Shutdown"/> re-closes every open window, so a per-instance flag would make each hidden
/// window (e.g. the other layout's window, kept alive but hidden by <c>ShellPresenter</c>) re-enter its own
/// "redirect to Shutdown" branch and call <see cref="Application.Shutdown"/> again.
/// </summary>
internal static class AppShutdownState
{
    public static bool InProgress { get; set; }
}

public partial class ShellWindow : Window
{
    private readonly IOsCapabilities _os;
    private readonly ThemeManager _theme;

    public ShellWindow(ShellViewModel viewModel, IOsCapabilities os, ThemeManager theme)
    {
        InitializeComponent();
        DataContext = viewModel;
        _os = os;
        _theme = theme;
        SourceInitialized += OnSourceInitialized;
        StateChanged += OnStateChanged;
        IsVisibleChanged += OnIsVisibleChanged;
        Activated += OnActivatedChanged;
        Deactivated += OnActivatedChanged;
        _theme.Changed += ApplyBackdrop;
    }

    private void OnActivatedChanged(object? sender, EventArgs e)
    {
        if (DataContext is ShellViewModel shell)
        {
            shell.Record.SetWindowActive(IsActive);
        }
    }

    // Distinct from OnStateChanged/minimize: fires on Show()/Hide() too, including ShellPresenter hiding this
    // window without minimizing it when swapping to the Compact layout, and the initial --tray launch (which
    // never calls Show() at all, so this simply never fires and RecordViewModel's default "not visible" state
    // stands) — see RecordViewModel.SetWindowVisible's doc comment for why minimize alone isn't enough.
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (DataContext is ShellViewModel shell)
        {
            if ((bool)e.NewValue)
            {
                shell.EnsureInitialPageLoaded();
            }

            // ShellWindow (RecordView) is the one layout that actually displays the preview image and audio
            // meters — see RecordViewModel.SetWindowVisible's doc comment for why CompactWindow passes false.
            shell.Record.SetWindowVisible((bool)e.NewValue, hostsPreviewSurfaces: true);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // §3.9: tear down the live preview while minimized.
        if (DataContext is ShellViewModel shell)
        {
            shell.Record.SetWindowMinimized(WindowState == WindowState.Minimized);
        }

        bool maximized = WindowState == WindowState.Maximized;
        MaxButtonIcon.Data = (System.Windows.Media.Geometry)FindResource(maximized ? "IconRestoreGeometry" : "IconMaximizeGeometry");
        string label = maximized ? RecMode.App.Resources.Strings.Shell_Restore : RecMode.App.Resources.Strings.Shell_Maximize;
        MaxButton.ToolTip = label;
        System.Windows.Automation.AutomationProperties.SetName(MaxButton, label);
    }

    private void OnSourceInitialized(object? sender, EventArgs e) => ApplyBackdrop();

    private void ApplyBackdrop()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        WindowBackdrop.SetDarkTitleBar(hwnd, _theme.IsDark);
        if (_os.SupportsMicaBackdrop)
        {
            WindowBackdrop.TryEnableMica(hwnd);
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // Explicit shutdown: the app runs under ShutdownMode.OnExplicitShutdown (so transient overlay windows and
    // tray-only operation don't end the process), so any path that closes this window must quit the app
    // itself. The caption X button routes here; Alt+F4 (or a taskbar/System-menu close) instead closes the
    // window directly via WM_SYSCOMMAND, bypassing this handler — OnClosing below catches that path too.
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!AppShutdownState.InProgress)
        {
            // Settings → General → "Close button minimizes to tray": the × hides instead of quitting, the
            // same fate minimize already gets unconditionally (TrayIconService). The tray menu's own "Quit"
            // always exits regardless, since it calls Application.Shutdown() directly rather than closing
            // this window.
            if (DataContext is ShellViewModel { Settings.CloseToTray: true })
            {
                e.Cancel = true;
                Hide();
                return;
            }

            // Redirect to a full app shutdown instead of letting this window merely close, which would
            // otherwise leave the process (and tray icon) running headless. Application.Shutdown() re-closes
            // every open window, including this one — the second time through, AppShutdownState.InProgress
            // lets it actually proceed.
            e.Cancel = true;
            AppShutdownState.InProgress = true;
            Application.Current.Shutdown();
            return;
        }

        base.OnClosing(e);
    }
}
