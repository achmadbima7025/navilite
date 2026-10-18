namespace NaviLite.Desktop.Models;

public sealed record SqlHistoryEntry(ulong Id, long StartedUnixMs, ulong DurationMs, string Operation, string Context, string Statement, bool Shortened, string Outcome)
{
    public string Time => DateTimeOffset.FromUnixTimeMilliseconds(StartedUnixMs).ToLocalTime().ToString("HH:mm:ss.fff");
    public string Duration => $"{DurationMs:N0} ms";
    public string Status => Outcome switch { "success" => "Success", "error" => "Error", _ => "Interrupted" };
}
