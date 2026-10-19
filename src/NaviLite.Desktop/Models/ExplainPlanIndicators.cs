namespace NaviLite.Desktop.Models;

public static class ExplainPlanIndicators
{
    public static string Detect(string accessType, string extra)
    {
        List<string> indicators = [];
        if (accessType.Trim().Equals("ALL", StringComparison.OrdinalIgnoreCase)) indicators.Add("Full scan (ALL)");
        if (extra.Contains("Using filesort", StringComparison.OrdinalIgnoreCase)) indicators.Add("Filesort");
        if (extra.Contains("Using temporary", StringComparison.OrdinalIgnoreCase)) indicators.Add("Temporary");
        return string.Join(" · ", indicators);
    }
}

public sealed class SqlResultRow(NativeRow row, string indicators)
{
    public string this[int column] => row[column];
    public string Indicators { get; } = indicators;
    public bool HasIndicators => Indicators.Length != 0;
}
