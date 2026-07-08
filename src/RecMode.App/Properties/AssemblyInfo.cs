using System.Runtime.CompilerServices;

// Lets RecMode.Recording.Tests exercise internal, pure/stateless classes (e.g. EncoderFallbackChain,
// BlackFrameDetector) directly rather than only via manual --selftest-* CLI hooks.
[assembly: InternalsVisibleTo("RecMode.Recording.Tests")]
