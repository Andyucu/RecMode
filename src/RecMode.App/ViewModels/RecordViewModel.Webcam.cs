using System.Collections.ObjectModel;
using RecMode.Capture;
using RecMode.Capture.Webcam;
using RecMode.Core.Recording;
using RecMode.Core.Settings;

namespace RecMode.App.ViewModels;

public sealed partial class RecordViewModel
{
    private WebcamCaptureSource? _previewWebcam;
    private WebcamDevice? _selectedWebcamDevice;
    private bool _webcamEnabled;
    private WebcamOverlayPosition _webcamPosition;
    private int _webcamSizePercent;
    private bool _loadingWebcamDevices;

    public ObservableCollection<WebcamDevice> WebcamDevices { get; } = [];
    public IReadOnlyList<WebcamOverlayPosition> WebcamPositions { get; } =
        [WebcamOverlayPosition.BottomRight, WebcamOverlayPosition.BottomLeft, WebcamOverlayPosition.TopRight, WebcamOverlayPosition.TopLeft];
    public bool HasWebcamDevices => WebcamDevices.Count > 0;

    public bool WebcamEnabled
    {
        get => _webcamEnabled;
        set
        {
            if (!SetProperty(ref _webcamEnabled, value))
            {
                return;
            }

            _settings.Current.WebcamEnabled = value;
            _settings.RequestSave();
            if (value)
            {
                StartWebcamPreview();
            }
            else
            {
                StopWebcamPreview();
            }
        }
    }

    public WebcamDevice? SelectedWebcamDevice
    {
        get => _selectedWebcamDevice;
        set
        {
            if (!SetProperty(ref _selectedWebcamDevice, value) || _loadingWebcamDevices)
            {
                return;
            }

            _settings.Current.WebcamDeviceId = value?.Id;
            _settings.RequestSave();
            if (WebcamEnabled)
            {
                StopWebcamPreview();
                StartWebcamPreview();
            }

            if (IsWebcamSource)
            {
                RestartPreview();
                RecordCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public WebcamOverlayPosition WebcamPosition
    {
        get => _webcamPosition;
        set
        {
            if (!SetProperty(ref _webcamPosition, value))
            {
                return;
            }

            _settings.Current.WebcamPosition = value;
            _settings.RequestSave();
            ApplyWebcamOverlayToPreview();
        }
    }

    public int WebcamSizePercent
    {
        get => _webcamSizePercent;
        set
        {
            if (!SetProperty(ref _webcamSizePercent, value))
            {
                return;
            }

            _settings.Current.WebcamSizePercent = value;
            _settings.RequestSave();
            ApplyWebcamOverlayToPreview();
        }
    }

    private async void LoadWebcamDevices()
    {
        try
        {
            IReadOnlyList<WebcamDevice> devices = await WebcamEnumerator.FindAllAsync();
            if (!_isActivePage)
            {
                return; // navigated away while enumerating
            }

            _loadingWebcamDevices = true;
            try
            {
                WebcamDevices.Clear();
                foreach (WebcamDevice d in devices)
                {
                    WebcamDevices.Add(d);
                }

                string? savedId = _settings.Current.WebcamDeviceId;
                _selectedWebcamDevice = string.IsNullOrEmpty(savedId)
                    ? WebcamDevices.FirstOrDefault()
                    : WebcamDevices.FirstOrDefault(d => d.Id == savedId) ?? WebcamDevices.FirstOrDefault();
                OnPropertyChanged(nameof(SelectedWebcamDevice));
                OnPropertyChanged(nameof(HasWebcamDevices));
            }
            finally
            {
                _loadingWebcamDevices = false;
            }
        }
        catch (Exception)
        {
            // Enumeration is best-effort — leaves WebcamDevices empty; the UI hides "Enable webcam" then.
        }
    }

    // Set synchronously the moment activation is kicked off, not after — _previewWebcam alone isn't a
    // sufficient re-entrancy guard because it's only assigned once StartWebcamPreviewAsync's await
    // completes. Without this, toggling the webcam off/on (or switching devices) again while a first
    // activation is still in flight passes the "_previewWebcam is not null" check a second time (still
    // null), starts a second WebcamCaptureSource, and whichever one's await finishes last silently
    // overwrites _previewWebcam without ever stopping the other — a leaked live camera session (LED stays
    // on) for the process's remaining lifetime.
    private bool _webcamStarting;

    private void StartWebcamPreview()
    {
        if (_previewWebcam is not null || _webcamStarting || _preview is null || !_webcamEnabled || SelectedWebcamDevice is null)
        {
            return;
        }

        _webcamStarting = true;
        StartWebcamPreviewAsync(SelectedWebcamDevice);
    }

    private async void StartWebcamPreviewAsync(WebcamDevice device)
    {
        var webcam = new WebcamCaptureSource();
        try
        {
            await webcam.StartAsync(device.Id);

            if (!_webcamEnabled || _preview is null || !Equals(SelectedWebcamDevice, device))
            {
                webcam.Stop(); // state changed while awaiting activation — discard
                return;
            }

            _previewWebcam = webcam;
            ApplyWebcamOverlayToPreview();
        }
        catch (Exception)
        {
            // Camera unavailable/busy, or a failure applying the overlay — preview still works without it.
            // Broad catch is deliberate: this method is `async void` (StartWebcamPreview can't await it
            // without becoming async itself), so anything escaping here would otherwise be a genuinely
            // unhandled exception with no one able to observe it.
            webcam.Stop();
            if (ReferenceEquals(_previewWebcam, webcam))
            {
                _previewWebcam = null;
            }
        }
        finally
        {
            _webcamStarting = false;
        }
    }

    private void StopWebcamPreview()
    {
        _previewWebcam?.Stop();
        _previewWebcam = null;
        _preview?.SetWebcamOverlay(null, null);
    }

    private void ApplyWebcamOverlayToPreview()
    {
        if (_preview is null || _previewWebcam is null)
        {
            return;
        }

        (int x, int y, int w, int h) = WebcamOverlayLayout.ComputeRect(_preview.Width, _preview.Height, WebcamSizePercent, WebcamPosition);
        _preview.SetWebcamOverlay(_previewWebcam, new RegionRect(x, y, w, h));
    }
}
