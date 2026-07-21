using System.Runtime.CompilerServices;

// Lets RecMode.Recording.Tests exercise internal, pure/stateless classes (e.g. EncoderFallbackChain,
// BlackFrameDetector) directly rather than only via manual --selftest-* CLI hooks.
[assembly: InternalsVisibleTo("RecMode.Recording.Tests")]

// Lets RecMode.App.Tests exercise pure logic that was private-only for no real reason (formatting helpers,
// path/parsing logic) — bumped to internal, not made public, since nothing outside RecMode.App needs it.
[assembly: InternalsVisibleTo("RecMode.App.Tests")]
