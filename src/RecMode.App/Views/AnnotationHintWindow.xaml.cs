using System.Windows;
using RecMode.App.Services;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Views;

/// <summary>
/// Small floating "exit draw mode" hint shown alongside <see cref="AnnotationOverlay"/>. The overlay itself
/// can't host a visible exit control — it's deliberately not excluded from capture so the ink is part of the
/// recording, and any button drawn on that same window would be recorded too. This is a separate,
/// capture-excluded window with a real button (plus a reminder that Esc also works), positioned top-centre
/// so it's never covered by the full-screen ink surface underneath it.
/// </summary>
public partial class AnnotationHintWindow : Window
{
    private readonly IOsCapabilities _os;
    private readonly Action _onExit;

    public AnnotationHintWindow(Action onExit, IOsCapabilities os)
    {
        InitializeComponent();
        _onExit = onExit;
        _os = os;
        SourceInitialized += (_, _) => CaptureExclusion.Apply(this, _os);
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Rect work = SystemParameters.WorkArea;
        Left = work.Left + (work.Width - ActualWidth) / 2;
        Top = work.Top + 28;
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => _onExit();
}
