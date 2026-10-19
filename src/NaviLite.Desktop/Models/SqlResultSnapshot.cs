using System.ComponentModel;

namespace NaviLite.Desktop.Models;

public sealed class SqlResultSnapshot : INotifyPropertyChanged, IDisposable
{
    private readonly ulong _sequence;
    private readonly string _context;
    private readonly long _elapsedMs;
    private readonly int? _statementIndex;

    public SqlResultSnapshot(ulong sequence, NativePage page, string context, long elapsedMs, int? statementIndex, bool isExplain = false)
    {
        _sequence = sequence;
        _context = context;
        _elapsedMs = elapsedMs;
        _statementIndex = statementIndex;
        Page = page;
        IsExplain = isExplain;
        int accessType = FindColumn(page, "type"), extra = FindColumn(page, "Extra");
        Rows = page.Rows.Select(row => new SqlResultRow(row, isExplain
            ? ExplainPlanIndicators.Detect(Marker(row, accessType), Marker(row, extra)) : string.Empty)).ToArray();
        int marked = Rows.Count(row => row.HasIndicators);
        HasPlanIndicators = marked != 0;
        PlanSummary = $"Optimizer estimates · {marked:N0} loaded plan row(s) show ALL, filesort, or temporary. These markers alone do not determine query performance.";
        if (accessType < 0 || extra < 0 || page.HasMore || page.TruncatedCells != 0)
            PlanSummary += " Indicators are incomplete: some plan rows, fields, or expected columns are unavailable.";
    }

    private static int FindColumn(NativePage page, string name) =>
        Enumerable.Range(0, page.Columns.Count).FirstOrDefault(index => page.Columns[index].Name.Equals(name, StringComparison.OrdinalIgnoreCase), -1);

    private static string Marker(NativeRow row, int column)
    {
        if (column < 0) return string.Empty;
        var cell = row.Cell(column);
        return cell.IsNull || cell.IsBinary || cell.IsTruncated ? string.Empty : cell.Text;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public NativePage Page { get; }
    public IReadOnlyList<SqlResultRow> Rows { get; }
    public bool IsExplain { get; }
    public bool HasPlanIndicators { get; }
    public string PlanSummary { get; }
    public bool IsPinned { get; private set; }
    public string Title => $"{(IsPinned ? "📌 " : string.Empty)}#{_sequence} · {(IsExplain ? "EXPLAIN" : "SELECT")} · {Page.Rows.Count:N0} rows";
    public string Summary => $"{(IsExplain ? "EXPLAIN plan" : "Snapshot")} · {_context}{(_statementIndex is { } index ? $" · statement {index}" : string.Empty)} · {_elapsedMs:N0} ms · {Page.ByteLength / 1024.0:N1} KiB · {Page.TruncatedCells:N0} shortened cells" +
        (Page.HasMore ? IsExplain ? " · plan shortened at 1,000 rows" : " · more rows available; narrow the query or change its LIMIT" : string.Empty);

    internal void TogglePin()
    {
        IsPinned = !IsPinned;
        PropertyChanged?.Invoke(this, new(nameof(IsPinned)));
        PropertyChanged?.Invoke(this, new(nameof(Title)));
    }

    public void Dispose() => Page.Dispose();
}
