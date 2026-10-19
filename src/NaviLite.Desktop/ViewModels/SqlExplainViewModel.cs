using System.Diagnostics;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public AsyncCommand ExplainSqlCommand { get; }

    private async Task ExplainSqlAsync()
    {
        if (!CanRunSql) return;
        ulong handle = _handle;
        string script = SqlText, database = SqlDatabase.Trim();
        string context = $"{ActiveConnection} · {(database.Length == 0 ? "no default database" : database)}";
        int caret = SqlCaretUtf16, start = SqlSelectionStartUtf16, length = SqlSelectionLengthUtf16;
        bool escapes = SqlBackslashEscapes, ansi = SqlAnsiQuotes, succeeded = false;
        await RunAsync(async () =>
        {
            SqlResultPaneIndex = 1;
            SqlRunStatus = "Requesting a tabular EXPLAIN plan in a fresh read-only session…";
            if (SqlResults.Count(result => result.IsPinned) >= MaximumSqlResults)
                throw new InvalidOperationException("Result tabs are limited to 8 including pinned snapshots. Close or unpin a result before explaining.");
            var elapsed = Stopwatch.StartNew();
            var result = await _core.ExplainSqlQueryAsync(handle, script, caret, start, length, database, escapes, ansi);
            if (_handle != handle) { result.Dispose(); throw new InvalidOperationException("The connection changed while explaining SQL."); }
            AddSqlResult(result, context, elapsed.ElapsedMilliseconds, null, true, isExplain: true);
            SqlRunStatus = "EXPLAIN completed · optimizer estimates with plan indicators. Pin this snapshot to compare plans across edits.";
            Status = "EXPLAIN completed. Query text is excluded from history.";
            succeeded = true;
        });
        if (!succeeded) SqlRunStatus = $"EXPLAIN failed. Previous snapshots retained. {Status}";
    }
}
