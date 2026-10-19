using System.Diagnostics;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private NativePage? _sqlResult;
    private string _sqlDatabase = string.Empty;
    private int _sqlRowLimit = 1000;
    private int _sqlResultPaneIndex;
    private string _sqlRunStatus = "Connect, choose an optional database, and run one SELECT from the caret or selected text. Read-only transaction · 10 second timeout.";

    public AsyncCommand RunSqlCommand { get; }
    public bool CanRunSql => CanBrowse && !string.IsNullOrWhiteSpace(SqlText);
    public int SqlSelectionStartUtf16 { get; set; }
    public int SqlSelectionLengthUtf16 { get; set; }
    public int[] SqlRowLimits { get; } = [100, 500, 1000];
    public string SqlDatabase { get => _sqlDatabase; set => Set(ref _sqlDatabase, value); }
    public int SqlRowLimit { get => _sqlRowLimit; set => Set(ref _sqlRowLimit, value); }
    public int SqlResultPaneIndex { get => _sqlResultPaneIndex; set => Set(ref _sqlResultPaneIndex, value); }
    public string SqlRunStatus { get => _sqlRunStatus; private set => Set(ref _sqlRunStatus, value); }
    public NativePage? SqlResult
    {
        get => _sqlResult;
        private set
        {
            var previous = _sqlResult;
            Set(ref _sqlResult, value);
            previous?.Dispose();
        }
    }

    private async Task RunSqlAsync()
    {
        if (!CanRunSql) return;
        ulong handle = _handle;
        bool succeeded = false;
        await RunAsync(async () =>
        {
            SqlResultPaneIndex = 1;
            SqlRunStatus = "Running SELECT in a fresh read-only transaction… Previous result remains until success.";
            var elapsed = Stopwatch.StartNew();
            string database = SqlDatabase.Trim();
            var result = await _core.ReadSqlQueryAsync(handle, SqlText, SqlCaretUtf16, SqlSelectionStartUtf16, SqlSelectionLengthUtf16,
                database, SqlRowLimit, SqlBackslashEscapes, SqlAnsiQuotes);
            if (_handle != handle) { result.Dispose(); throw new InvalidOperationException("The connection changed while running SQL."); }
            SqlResult = result;
            SqlRunStatus = $"Last successful run · {ActiveConnection} · {(database.Length == 0 ? "no default database" : database)} · {result.Rows.Count:N0} rows · {elapsed.ElapsedMilliseconds:N0} ms · {result.ByteLength / 1024.0:N1} KiB · {result.TruncatedCells:N0} shortened cells" +
                (result.HasMore ? " · more rows available; narrow the query or change its LIMIT" : string.Empty);
            Status = $"SELECT completed: {result.Rows.Count:N0} rows. Query text is excluded from history.";
            succeeded = true;
        });
        if (!succeeded) SqlRunStatus = $"Run failed. Previous result retained. {Status}";
    }

    private void ClearSqlResult()
    {
        SqlResult = null;
        SqlRunStatus = "Run one SELECT from the caret or selected text. Results are snapshots; editing does not rerun the query.";
    }
}
