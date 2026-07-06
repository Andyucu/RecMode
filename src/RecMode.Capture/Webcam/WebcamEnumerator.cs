using Windows.Devices.Enumeration;

namespace RecMode.Capture.Webcam;

/// <summary>Enumerates available webcams (device class VideoCapture).</summary>
public static class WebcamEnumerator
{
    public static async Task<IReadOnlyList<WebcamDevice>> FindAllAsync()
    {
        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        return devices.Select(d => new WebcamDevice(d.Id, d.Name)).ToList();
    }
}
