using System.Windows;
using RecMode.App.Services;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Views;

/// <summary>
/// Floating, always-on-top recording controls (plan Phase 5): rec dot, elapsed, pause/resume, screenshot,
/// live stats, stop. Excluded from capture so it never appears in the recording, and non-activating so it
/// doesn't steal focus from the app being recorded. Positioned bottom-centre of the primary display.
/// </summary>
public partial class RecordingToolbarWindow : Window
{
    private readonly IOsCapabilities _os;
    private readonly bool _excludeFromCapture;

    public RecordingToolbarWindow(object viewModel, IOsCapabilities os, bool excludeFromCapture = true)
    {
        InitializeComponent();
        DataContext = viewModel;
        _os = os;
        _excludeFromCapture = excludeFromCapture;
        SourceInitialized += (_, _) =>
        {
            if (_excludeFromCapture)
            {
                CaptureExclusion.Apply(this, _os);
            }
        };
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Bottom-centre of the primary work area (DIP units — above the taskbar).
        Rect work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Bottom - ActualHeight - 28;
    }
}
