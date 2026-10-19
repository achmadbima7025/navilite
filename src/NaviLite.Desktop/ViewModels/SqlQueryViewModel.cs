using System.Collections.ObjectModel;
using System.Diagnostics;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private const int MaximumSqlResults = 8;
    private const ulong MaximumSqlResultBytes = 32 * 1024 * 1024;
    private SqlResultSnapshot? _selectedSqlResult;
    private ulong _sqlResultSequence;
    private string _sqlDatabase = string.Empty;
    private int _sqlRowLimit = 1000;
    private int _sqlResultPaneIndex;
    private bool _sqlBatchRunning;
    private bool _stopSqlBatch;
    private string _sqlRunStatus = "Run SELECT from the caret or selection, or Run All for up to 8 SELECT statements. Pin snapshots to keep them across runs.";

    public AsyncCommand RunSqlCommand { get; }
    public AsyncCommand RunAllSqlCommand { get; }
    public AsyncCommand StopSqlBatchCommand { get; }
    public AsyncCommand PinSqlResultCommand { get; }
    public AsyncCommand CloseSqlResultCommand { get; }
    public AsyncCommand ClearUnpinnedSqlResultsCommand { get; }
    public ObservableCollection<SqlResultSnapshot> SqlResults { get; } = [];
    public bool CanRunSql => CanBrowse && !string.IsNullOrWhiteSpace(SqlText);
    public int SqlSelectionStartUtf16 { get; set; }
    public int SqlSelectionLengthUtf16 { get; set; }
    public int[] SqlRowLimits { get; } = [100, 500, 1000];
    public string SqlDatabase { get => _sqlDatabase; set => Set(ref _sqlDatabase, value); }
    public int SqlRowLimit { get => _sqlRowLimit; set => Set(ref _sqlRowLimit, value); }
    public int SqlResultPaneIndex { get => _sqlResultPaneIndex; set => Set(ref _sqlResultPaneIndex, value); }
    public string SqlRunStatus { get => _sqlRunStatus; private set => Set(ref _sqlRunStatus, value); }
    public string SqlResultsSummary => $"{SqlResults.Count}/8 results · {SqlResults.Count(result => result.IsPinned)} pinned · {SqlResults.Sum(result => (long)result.Page.ByteLength) / 1048576.0:N1}/32 MiB";
    public SqlResultSnapshot? SelectedSqlResult
    {
        get => _selectedSqlResult;
        set { Set(ref _selectedSqlResult, value); RefreshCommands(); }
    }

    private async Task RunSqlAsync(bool all = false)
    {
        if (!CanRunSql) return;
        ulong handle = _handle;
        string script = SqlText, database = SqlDatabase.Trim();
        string context = $"{ActiveConnection} · {(database.Length == 0 ? "no default database" : database)}";
        int caret = SqlCaretUtf16, selectionStart = SqlSelectionStartUtf16, selectionLength = SqlSelectionLengthUtf16, rowLimit = SqlRowLimit;
        bool escapes = SqlBackslashEscapes, ansi = SqlAnsiQuotes;
        bool succeeded = false;
        int completed = 0;
        _sqlBatchRunning = all;
        _stopSqlBatch = false;
        try
        {
            await RunAsync(async () =>
            {
                SqlResultPaneIndex = 1;
                SqlRunStatus = all ? "Checking every statement before Run All…" : "Running SELECT in a fresh read-only transaction…";
                List<SqlStatementSpan?> targets = [null];
                if (all)
                {
                    var prepared = await _core.PrepareSqlReadsAsync(script, escapes, ansi);
                    targets = prepared.Statements.Select(statement => (SqlStatementSpan?)statement).ToList();
                    SelectedSqlStatement = null;
                    SqlStatements.Clear();
                    foreach (var statement in prepared.Statements) SqlStatements.Add(statement);
                    SqlAnalysisStatus = $"{targets.Count} SELECT statements prepared · syntax is checked by the server during each run";
                }
                if (SqlResults.Count(result => result.IsPinned) + targets.Count > MaximumSqlResults)
                    throw new InvalidOperationException("Result tabs are limited to 8 including pinned snapshots. Close or unpin results before running this script.");
                foreach (var target in targets)
                {
                    if (all && _stopSqlBatch) break;
                    SqlRunStatus = all ? $"Running SELECT {completed + 1}/{targets.Count}… Each statement has an independent read-only session and 10 second timeout." : SqlRunStatus;
                    var elapsed = Stopwatch.StartNew();
                    var result = await _core.ReadSqlQueryAsync(handle, script, target?.StartUtf16 ?? caret,
                        target?.StartUtf16 ?? selectionStart, target?.LengthUtf16 ?? selectionLength, database, rowLimit, escapes, ansi);
                    if (_handle != handle) { result.Dispose(); throw new InvalidOperationException("The connection changed while running SQL."); }
                    AddSqlResult(result, context, elapsed.ElapsedMilliseconds, target?.Index, completed == 0);
                    completed++;
                }
                bool stopped = completed < targets.Count;
                SqlRunStatus = stopped ? $"Stopped after {completed}/{targets.Count} SELECT results. Completed and pinned snapshots remain available." : $"Completed · {completed} SELECT result(s). Pin a result to retain it on the next run. Separate statements may observe different database snapshots.";
                Status = stopped ? "Run All stopped after the current SELECT." : $"SELECT completed: {completed} result(s). Query text is excluded from history.";
                succeeded = true;
            });
            if (!succeeded) SqlRunStatus = all
                ? $"Run All stopped · {completed} result(s) retained from this run. {Status}"
                : $"Run failed. Previous results retained. {Status}";
        }
        finally { _sqlBatchRunning = false; RefreshCommands(); }
    }

    private void AddSqlResult(NativePage page, string context, long elapsedMs, int? statementIndex, bool replaceUnpinned, bool isExplain = false)
    {
        bool attached = false;
        try
        {
            var retained = SqlResults.Where(result => !replaceUnpinned || result.IsPinned).ToArray();
            ulong bytes = (ulong)retained.Sum(result => (long)result.Page.ByteLength);
            if (retained.Length >= MaximumSqlResults || bytes + page.ByteLength > MaximumSqlResultBytes)
                throw new InvalidOperationException("Result snapshots are limited to 8 tabs and 32 MiB in total. Close snapshots or reduce the row limit before running again.");
            var snapshot = new SqlResultSnapshot(++_sqlResultSequence, page, context, elapsedMs, statementIndex, isExplain);
            if (replaceUnpinned) RemoveSqlResults(false);
            SqlResults.Add(snapshot);
            attached = true;
            SelectedSqlResult = snapshot;
            NotifySqlResults();
        }
        finally { if (!attached) page.Dispose(); }
    }

    private Task StopSqlBatchAsync()
    {
        _stopSqlBatch = true;
        SqlRunStatus = "Stopping after the current SELECT finishes (up to its 10 second client timeout)…";
        RefreshCommands();
        return Task.CompletedTask;
    }

    private Task PinSqlResultAsync()
    {
        SelectedSqlResult?.TogglePin();
        NotifySqlResults();
        return Task.CompletedTask;
    }

    private Task CloseSqlResultAsync()
    {
        if (SelectedSqlResult is { } result)
        {
            SqlResults.Remove(result);
            SelectedSqlResult = SqlResults.LastOrDefault();
            result.Dispose();
            NotifySqlResults();
        }
        return Task.CompletedTask;
    }

    private Task ClearUnpinnedSqlResultsAsync()
    {
        RemoveSqlResults(false);
        return Task.CompletedTask;
    }

    private void RemoveSqlResults(bool includePinned)
    {
        foreach (var result in SqlResults.Where(result => includePinned || !result.IsPinned).ToArray())
        {
            SqlResults.Remove(result);
            result.Dispose();
        }
        if (SelectedSqlResult is null || !SqlResults.Contains(SelectedSqlResult)) SelectedSqlResult = SqlResults.LastOrDefault();
        NotifySqlResults();
    }

    private void NotifySqlResults()
    {
        Changed(nameof(SqlResultsSummary));
        RefreshCommands();
    }

    private void ClearSqlResult(bool includePinned = true)
    {
        RemoveSqlResults(includePinned);
        SqlRunStatus = "Run SELECT or Run All. Pinned snapshots survive edits, document replacement, and reruns; disconnect and exit release all results.";
    }
}
