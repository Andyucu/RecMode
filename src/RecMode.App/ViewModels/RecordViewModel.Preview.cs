using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RecMode.Capture;
using RecMode.Capture.Webcam;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    private IPreviewEngine? _preview;
    private WriteableBitmap? _previewBitmap;
    private byte[] _previewBuffer = [];
    private ImageSource? _previewImage;

    // Defaults match WgcPreviewEngine's own pre-existing hardcoded cap, so behavior is unchanged until the
    // view actually reports its real on-screen size (e.g. under a headless self-test, which never does).
    private int _previewMaxWidth = 1280;
    private int _previewMaxHeight = 720;
    private DispatcherTimer? _previewResizeDebounce;

    /// <summary>Called by <c>RecordView</c>'s codebehind whenever the preview card's on-screen pixel size
    /// changes. Rendering the capture preview at a fixed 1280×720 regardless of how small the card is actually
    /// laid out wastes GPU scale + readback + bitmap-upload work for pixels that were never going to be shown
    /// at that resolution — so the preview engine is asked to fit within the card's real size instead (see
    /// <see cref="StartPreview"/>). Debounced (400 ms) rather than restarting the capture session on every
    /// tick of an interactive window drag.</summary>
    public void SetPreviewSurfaceSize(int pixelWidth, int pixelHeight)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0 ||
            (pixelWidth == _previewMaxWidth && pixelHeight == _previewMaxHeight))
        {
            return;
        }

        _previewMaxWidth = pixelWidth;
        _previewMaxHeight = pixelHeight;

        if (_preview is null)
        {
            return; // next StartPreview() picks up the new size directly
        }

        _previewResizeDebounce?.Stop();
        _previewResizeDebounce ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
        _previewResizeDebounce.Tick -= OnPreviewResizeSettled;
        _previewResizeDebounce.Tick += OnPreviewResizeSettled;
        _previewResizeDebounce.Start();
    }

    private void OnPreviewResizeSettled(object? sender, EventArgs e)
    {
        _previewResizeDebounce?.Stop();
        RestartPreview();
    }

    public ImageSource? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }
    public bool HasPreview => PreviewImage is not null;

    /// <summary>§3.9: never run when nothing can see it — not active-paged, minimized, the hosting window
    /// (Shell/Compact) not actually shown at all (e.g. a <c>--tray</c> launch), or the shown window not being
    /// one that displays a preview surface in the first place (<c>CompactWindow.xaml</c> has none). See
    /// <see cref="SetWindowVisible"/>. Re-checked after any await, since all of these can change while a
    /// camera is still activating.</summary>
    private bool CanRunPreview =>
        !IsRecording && PreviewEligibility.CanRun(_isActivePage, IsWindowMinimized, IsWindowVisible, _hostsPreviewSurfaces);

    /// <summary>Set synchronously the moment an asynchronous preview start begins — <see cref="_preview"/>
    /// alone isn't a sufficient re-entrancy guard because it's only assigned once activation completes. Same
    /// reasoning as <c>_webcamStarting</c> on the picture-in-picture path.</summary>
    private bool _previewStarting;

    private void StartPreview()
    {
        if (_preview is not null || _previewStarting || !CanRunPreview)
        {
            return;
        }

        CaptureTarget? target = CurrentTarget();
        if (target is null)
        {
            return;
        }

        // IsSupported() is a Windows.Graphics.Capture check, so it must only gate the WGC-backed engine.
        // A Webcam source runs WebcamPreviewEngine, which never touches WGC or D3D11 — gating it here meant
        // that on exactly the VM/RDP configurations GdiCaptureEngine exists to serve (and in this dev
        // sandbox's own DXGI_ERROR_UNSUPPORTED state), selecting Webcam showed a permanently blank preview
        // while recording worked fine, since RecordingCoordinator has no equivalent gate.
        if (target.Kind != CaptureKind.Webcam && !CaptureCapabilities.IsSupported())
        {
            return;
        }

        // Starting a webcam preview activates the camera for real (MediaCapture.InitializeAsync + frame
        // reader), which takes long enough to stall the UI noticeably on every nav to Record, restore from
        // minimize, source switch, and stop-recording. Do that part off the dispatcher. The WGC path stays
        // synchronous — it's fast, and it's the well-exercised one.
        if (target.Kind == CaptureKind.Webcam)
        {
            _previewStarting = true;
            StartWebcamSourcePreviewAsync(target);
            return;
        }

        try
        {
            IPreviewEngine engine = _previewFactory();
            engine.Start(target, _settings.Current.CaptureCursor, _previewMaxWidth, _previewMaxHeight);
            _previewBuffer = new byte[engine.ByteSize];
            _previewBitmap = new WriteableBitmap(engine.Width, engine.Height, 96, 96, PixelFormats.Bgra32, null);
            engine.FrameAvailable += OnPreviewFrame;
            _preview = engine;
            _preview.SetBrightness(Brightness);
            PreviewImage = _previewBitmap;
            OnPropertyChanged(nameof(HasPreview));
            StartWebcamPreview();
        }
        catch (Exception)
        {
            StopPreview(); // preview is best-effort; the record path still works
        }
    }

    /// <summary>
    /// Webcam-as-source preview start, with the camera activation moved off the UI thread. Deliberately
    /// <c>async void</c> (nothing can await it — <see cref="StartPreview"/> is called from property setters
    /// and lifecycle hooks), so the catch is broad: anything escaping here would otherwise be an
    /// unobservable unhandled exception. Every §3.9 condition is re-checked after the await, because the user
    /// can navigate away, minimize, start recording, or switch source while the camera is still activating.
    /// </summary>
    private async void StartWebcamSourcePreviewAsync(CaptureTarget target)
    {
        var engine = new WebcamPreviewEngine();
        try
        {
            bool captureCursor = _settings.Current.CaptureCursor;
            int maxWidth = _previewMaxWidth, maxHeight = _previewMaxHeight;
            await System.Threading.Tasks.Task.Run(() => engine.Start(target, captureCursor, maxWidth, maxHeight));

            // Resumed on the dispatcher: the WriteableBitmap below is UI-thread-affine.
            if (_preview is not null || !CanRunPreview || !target.Equals(CurrentTarget(refreshFollowedWindow: false)))
            {
                engine.Dispose(); // state moved on while the camera was activating — discard
                return;
            }

            _previewBuffer = new byte[engine.ByteSize];
            _previewBitmap = new WriteableBitmap(engine.Width, engine.Height, 96, 96, PixelFormats.Bgra32, null);
            engine.FrameAvailable += OnPreviewFrame;
            _preview = engine;
            PreviewImage = _previewBitmap;
            OnPropertyChanged(nameof(HasPreview));
            // No StartWebcamPreview() here: webcam-as-source already IS the camera feed, so the
            // picture-in-picture overlay would be pointless and redundant. See
            // RecordingCoordinator.SetupWebcamOverlay's matching guard.
        }
        catch (Exception)
        {
            engine.Dispose();
            if (ReferenceEquals(_preview, engine))
            {
                _preview = null;
            }
        }
        finally
        {
            _previewStarting = false;

            // If this activation was discarded because the selected source changed while the camera was
            // still activating (the "target moved on" branch above), _preview is still null and nothing else
            // was ever told to pick up whatever the *new* current target is — the preview pane stayed on its
            // "Preview paused" placeholder for the rest of the visit, until some unrelated property change
            // happened to call RestartPreview() again. StartPreview() re-reads CurrentTarget() itself, so
            // this correctly targets whatever is selected now, not the stale target being activated above.
            // No-ops harmlessly on the success path (_preview is already set) and when nothing should be
            // previewing right now (CanRunPreview false).
            if (_preview is null && CanRunPreview)
            {
                StartPreview();
            }
        }
    }

    private void StopPreview()
    {
        StopWebcamPreview();

        if (_preview is null)
        {
            return;
        }

        _preview.FrameAvailable -= OnPreviewFrame;
        _preview.Stop();
        _preview.Dispose();
        _preview = null;
        _previewBitmap = null;
        PreviewImage = null;
        OnPropertyChanged(nameof(HasPreview));
    }

    private void RestartPreview()
    {
        if (_preview is null && (!_isActivePage || IsRecording))
        {
            return;
        }

        StopPreview();
        StartPreview();
    }

    private void OnPreviewFrame() => DispatchLowPriority(() =>
    {
        if (_preview is null || _previewBitmap is null)
        {
            return;
        }

        if (_preview.TryGetLatestFrame(_previewBuffer))
        {
            var rect = new Int32Rect(0, 0, _previewBitmap.PixelWidth, _previewBitmap.PixelHeight);
            _previewBitmap.WritePixels(rect, _previewBuffer, _preview.Stride, 0);
        }
    });
}
