namespace RecMode.Capture.Webcam;

/// <summary>A video-capture device (webcam) available for the picture-in-picture overlay.</summary>
public sealed record WebcamDevice(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}
