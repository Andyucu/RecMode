namespace RecMode.App.Services;

/// <summary>
/// Parsed command-line automation flags (plan §3, Phase 5 CLI). Recognised: <c>--record</c>, <c>--stop</c>,
/// <c>--screenshot</c>, <c>--tray</c>. Unknown args are ignored so the parser is forward-compatible.
/// </summary>
public sealed record CommandLineOptions(bool Record, bool Stop, bool Screenshot, bool Tray)
{
    /// <summary>True when the command line asks for a recording/screenshot action (as opposed to just launching).</summary>
    public bool HasAction => Record || Stop || Screenshot;

    public static CommandLineOptions Parse(IEnumerable<string> args)
    {
        bool record = false, stop = false, screenshot = false, tray = false;

        foreach (string raw in args)
        {
            switch (raw.Trim().ToLowerInvariant())
            {
                case "--record" or "-r": record = true; break;
                case "--stop" or "-s": stop = true; break;
                case "--screenshot": screenshot = true; break;
                case "--tray": tray = true; break;
            }
        }

        return new CommandLineOptions(record, stop, screenshot, tray);
    }
}
