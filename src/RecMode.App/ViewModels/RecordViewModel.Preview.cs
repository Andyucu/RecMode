using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RecMode.Capture;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    private IPreviewEngine? _preview;
    private WriteableBitmap? _previewBitmap;
    private byte[] _previewBuffer = [];
    private ImageSource? _previewImage;

    public ImageSource? PreviewImage { get => _previewImage; private set => SetProperty(ref _previewImage, value); }
    public bool HasPreview => PreviewImage is not null;

    private void StartPreview()
    {
        if (_preview is not null || IsRecording || !_isActivePage)
        {
            return;
        }

        CaptureTarget? target = CurrentTarget();
        if (target is null || !CaptureCapabilities.IsSupported())
        {
            return;
        }

        try
        {
            IPreviewEngine engine = _previewFactory();
            engine.Start(target, _settings.Current.CaptureCursor);
            _previewBuffer = new byte[engine.ByteSize];
            _previewBitmap = new WriteableBitmap(engine.Width, engine.Height, 96, 96, PixelFormats.Bgra32, null);
            engine.FrameAvailable += OnPreviewFrame;
            _preview = engine;
            PreviewImage = _previewBitmap;
            OnPropertyChanged(nameof(HasPreview));
            StartWebcamPreview();
        }
        catch (Exception)
        {
            StopPreview(); // preview is best-effort; the record path still works
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

    private void OnPreviewFrame() => Dispatch(() =>
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
