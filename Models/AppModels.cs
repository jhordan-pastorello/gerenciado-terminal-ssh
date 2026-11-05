namespace StorageTerm.Models;

public enum ConnectionMode
{
    Auto = 0,
    Lan = 1,
    External = 2
}

public enum PaneStatus
{
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
    Stopped
}

public enum ConnectionKind
{
    Lan,
    External,
    Demo
}

public sealed record ConnectionTarget(string Alias, ConnectionMode Mode, bool LanReachable, int LatencyMs)
{
    public ConnectionKind Kind => Alias == "demo" ? ConnectionKind.Demo : Mode == ConnectionMode.Lan ? ConnectionKind.Lan : ConnectionKind.External;
}

public sealed record ProbeResult(bool LanReachable, int LatencyMs, string? Error);

public sealed class StartupOptions
{
    public bool StartInTray { get; init; }
    public bool Demo { get; init; }
    public int RequestedPanes { get; init; }

    public static StartupOptions Parse(string[] args)
    {
        var tray = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));
        var demo = args.Any(a => a.Equals("--demo", StringComparison.OrdinalIgnoreCase));
        var panes = 1;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--panes=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg["--panes=".Length..], out var value))
            {
                panes = Math.Clamp(value, 1, 6);
            }
        }

        return new StartupOptions
        {
            StartInTray = tray,
            Demo = demo,
            RequestedPanes = panes
        };
    }
}
