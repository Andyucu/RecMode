using System.Runtime.CompilerServices;

// Lets RecMode.Encoding.Tests exercise internal, pure/stateless logic (e.g. EncoderProbe's disk-cache
// fingerprint matching) directly, without needing a real ffmpeg.exe to spawn processes against.
[assembly: InternalsVisibleTo("RecMode.Encoding.Tests")]
