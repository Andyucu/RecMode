using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using RecMode.App.Services;
using RecMode.Core.Infrastructure;
using RecMode.Core.Settings;

namespace RecMode.App.Views;

/// <summary>
/// Floating, always-on-top recording controls (plan Phase 5): rec dot, elapsed, pause/resume, screenshot,
/// live stats, stop. Excluded from capture so it never appears in the recording, and non-activating so it
/// doesn't steal focus from the app being recorded. Draggable from anywhere on its background (left/right
/// grip dots make this obvious) to any position on any monitor. Resets to bottom-centre of the primary
/// display on every new recording unless the Pin toggle is on, in which case it reopens at the last pinned
/// position instead — direct user request: predictable by default, "remember this spot" as an explicit opt-in.
/// </summary>
public partial class RecordingToolbarWindow : Window
{
    private readonly IOsCapabilities _os;
    private readonly ISettingsService _settings;
    private readonly bool _excludeFromCapture;

    public RecordingToolbarWindow(object viewModel, IOsCapabilities os, ISettingsService settings, bool excludeFromCapture = true)
    {
        InitializeComponent();
        DataContext = viewModel;
        _os = os;
        _settings = settings;
        _excludeFromCapture = excludeFromCapture;
        SourceInitialized += (_, _) =>
        {
            if (_excludeFromCapture)
            {
                CaptureExclusion.Apply(this, _os);
            }
        };
        Loaded += OnLoaded;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        LocationChanged += OnLocationChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RecModeSettings s = _settings.Current;
        PinToggle.IsChecked = s.ToolbarPinned;

        if (s.ToolbarPinned && s.ToolbarLeft.HasValue && s.ToolbarTop.HasValue)
        {
            Left = s.ToolbarLeft.Value;
            Top = s.ToolbarTop.Value;
            return;
        }

        // Bottom-centre of the primary work area (DIP units — above the taskbar).
        Rect work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Bottom - ActualHeight - 28;
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Left-drag anywhere on the toolbar (background, or the left/right grip areas) moves the window;
        // buttons mark their own MouseLeftButtonDown handled internally, so this only fires when nothing
        // else claimed the click first.
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // DragMove throws if called outside an active left-button-down gesture (e.g. a synthesized
            // event) — harmless, just skip the move.
        }
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_settings.Current.ToolbarPinned)
        {
            // Not pinned: the window still moves live for this session, but nothing is persisted — the next
            // recording resets to the default position, per the "predictable by default" behavior above.
            return;
        }

        _settings.Current.ToolbarLeft = Left;
        _settings.Current.ToolbarTop = Top;
        _settings.RequestSave();
    }

    private void OnPinToggled(object sender, RoutedEventArgs e)
    {
        bool pinned = ((ToggleButton)sender).IsChecked == true;
        _settings.Current.ToolbarPinned = pinned;
        if (pinned)
        {
            // Lock in wherever it's sitting right now, rather than requiring a drag first.
            _settings.Current.ToolbarLeft = Left;
            _settings.Current.ToolbarTop = Top;
        }

        _settings.RequestSave();
    }
}
