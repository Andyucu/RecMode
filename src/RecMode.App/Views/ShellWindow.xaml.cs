using System.Windows;
using System.Windows.Interop;
using RecMode.App.Themes;
using RecMode.App.ViewModels;
using RecMode.Core.Infrastructure;
using RecMode.Interop.Windowing;

namespace RecMode.App.Views;

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
        _theme.Changed += ApplyBackdrop;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // §3.9: tear down the live preview while minimized.
        if (DataContext is ShellViewModel shell)
        {
            shell.Record.SetWindowMinimized(WindowState == WindowState.Minimized);
        }
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
    // tray-only operation don't end the process), so the main window's close button must quit the app itself.
    private void OnClose(object sender, RoutedEventArgs e) => Application.Current.Shutdown();
}
