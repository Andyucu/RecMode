using System.Diagnostics;
using System.IO.Pipes;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RecMode.Spike;

/// <summary>
/// System-audio loopback (WASAPI) for the spike's second pipe. Captured bytes are forwarded to ffmpeg
/// f32le; a wall-clock-paced writer pads silence when loopback is idle so the audio stream stays aligned
/// to the QPC timeline (WASAPI loopback delivers nothing during silence, which would otherwise desync
/// A/V). Disposable spike code; the productionized mixer lands in Phase 4.
/// </summary>
internal sealed class AudioLoopback : IDisposable
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly Lock _lock = new();
    private readonly Queue<byte[]> _queue = new();
    private byte[] _head = [];
    private int _headOffset;

    public int SampleRate { get; }
    public int Channels { get; }
    public bool IsFloat { get; }
    public int BytesPerSecond { get; }

    public AudioLoopback()
    {
        _capture = new WasapiLoopbackCapture(); // default render device, event-driven
        WaveFormat wf = _capture.WaveFormat;
        SampleRate = wf.SampleRate;
        Channels = wf.Channels;
        IsFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat
            || (wf.Encoding == WaveFormatEncoding.Extensible && wf.BitsPerSample == 32);
        BytesPerSecond = SampleRate * Channels * 4; // f32le

        _capture.DataAvailable += (_, e) =>
        {
            if (e.BytesRecorded <= 0) return;
            var buf = new byte[e.BytesRecorded];
            Array.Copy(e.Buffer, buf, e.BytesRecorded);
            lock (_lock) { _queue.Enqueue(buf); }
        };
    }

    public void Start() => _capture.StartRecording();

    /// <summary>
    /// Writes wall-clock-paced f32le to <paramref name="pipe"/> until <paramref name="token"/> trips,
    /// padding with silence on underflow. Returns total bytes written.
    /// </summary>
    public long PumpUntil(NamedPipeServerStream pipe, CancellationToken token)
    {
        byte[] scratch = new byte[65536];
        byte[] silence = new byte[65536];
        long start = Stopwatch.GetTimestamp();
        long written = 0;
        int frame = Channels * 4;

        while (!token.IsCancellationRequested)
        {
            long elapsed = Stopwatch.GetTimestamp() - start;
            long target = (long)(elapsed / (double)Stopwatch.Frequency * BytesPerSecond);
            target -= target % frame;

            while (written < target)
            {
                int need = (int)Math.Min(target - written, scratch.Length);
                int n = Dequeue(scratch, need);
                if (n == 0)
                {
                    int pad = Math.Min(need, silence.Length);
                    pipe.Write(silence, 0, pad);
                    written += pad;
                }
                else
                {
                    pipe.Write(scratch, 0, n);
                    written += n;
                }
            }

            Thread.Sleep(5);
        }

        return written;
    }

    private int Dequeue(byte[] dest, int max)
    {
        lock (_lock)
        {
            int copied = 0;
            while (copied < max)
            {
                if (_head.Length - _headOffset == 0)
                {
                    if (_queue.Count == 0) break;
                    _head = _queue.Dequeue();
                    _headOffset = 0;
                }

                int avail = _head.Length - _headOffset;
                int take = Math.Min(avail, max - copied);
                Array.Copy(_head, _headOffset, dest, copied, take);
                _headOffset += take;
                copied += take;
            }

            return copied;
        }
    }

    public void Dispose()
    {
        try { _capture.StopRecording(); } catch (Exception) { /* spike */ }
        _capture.Dispose();
    }
}
