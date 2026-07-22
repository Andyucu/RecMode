using System.Windows;
using RecMode.App.Views;
using RecMode.Core.Settings;

namespace RecMode.App.Services;

/// <summary>
/// Owns which top-level window represents "the app": the full <see cref="ShellWindow"/> (Sidebar/TopTab
/// layouts) or the small always-on-top <see cref="CompactWindow"/> (Compact layout — plan §1 "compact
/// launcher"). Swaps between them live when the layout setting changes, without restarting the app.
/// </summary>
public sealed class ShellPresenter
{
    private readonly ISettingsService _settings;
    private readonly ShellWindow _shell;
    private readonly Func<CompactWindow> _compactFactory;
    private CompactWindow? _compact;
    private readonly Dictionary<Window, WindowState> _lastVisibleStates = [];

    /// <summary>Raised whenever <see cref="Current"/> changes to a different window instance.</summary>
    public event Action? CurrentChanged;

    public ShellPresenter(ISettingsService settings, ShellWindow shell, Func<CompactWindow> compactFactory)
    {
        _settings = settings;
        _shell = shell;
        _compactFactory = compactFactory;
        TrackWindow(_shell);
        Current = _settings.Current.Layout == ShellLayout.Compact ? EnsureCompact() : _shell;
        _settings.SettingsChanged += (_, _) => ApplyLayout();
    }

    /// <summary>The window that currently represents the app — swaps live as <see cref="CurrentChanged"/> fires.</summary>
    public Window Current { get; private set; }

    /// <summary>Shows and activates <see cref="Current"/> (startup, tray restore, CLI-forwarded commands).</summary>
    public void Show()
    {
        Current.Show();
        if (Current.WindowState == WindowState.Minimized)
        {
            Current.WindowState = _lastVisibleStates.GetValueOrDefault(Current, WindowState.Normal);
        }
        Current.Activate();
    }

    private void ApplyLayout()
    {
        bool wantCompact = _settings.Current.Layout == ShellLayout.Compact;
        bool isCompact = ReferenceEquals(Current, _compact);
        if (wantCompact == isCompact)
        {
            return; // Sidebar<->TopTab changes are handled reactively inside ShellWindow itself; no window swap.
        }

        bool wasVisible = Current.IsVisible;
        Current.Hide();
        Current = wantCompact ? EnsureCompact() : _shell;
        Application.Current.MainWindow = Current; // several dialogs (region picker, profile/schedule prompts,
                                                   // countdown) use MainWindow as their Owner
        CurrentChanged?.Invoke();
        if (wasVisible)
        {
            Show();
        }
    }

    private CompactWindow EnsureCompact()
    {
        if (_compact is null)
        {
            _compact = _compactFactory();
            TrackWindow(_compact);
        }
        return _compact;
    }

    private void TrackWindow(Window window) => window.StateChanged += (_, _) =>
    {
        if (window.WindowState != WindowState.Minimized)
        {
            _lastVisibleStates[window] = window.WindowState;
        }
    };
}
