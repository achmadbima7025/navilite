using System.Collections.ObjectModel;
using System.IO;
using NaviLite.Desktop.Models;
using NaviLite.Desktop.Services;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private string _sqlText = string.Empty;
    private string _savedSqlText = string.Empty;
    private string? _sqlPath;
    private bool _sqlBackslashEscapes = true;
    private bool _sqlAnsiQuotes;
    private SqlStatementSpan? _selectedSqlStatement;
    private string _sqlAnalysisStatus = "Analyze statement boundaries offline, or connect and Run SELECT from the caret or selected text.";

    public event Action<SqlStatementSpan>? SqlSelectionRequested;
    public AsyncCommand AnalyzeSqlCommand { get; }
    public AsyncCommand SelectCurrentSqlCommand { get; }
    public ObservableCollection<SqlStatementSpan> SqlStatements { get; } = [];
    public bool CanUseSqlEditor => _coreReady && !IsBusy;
    public int SqlCaretUtf16 { get; set; }
    public string? SqlPath => _sqlPath;
    public bool IsSqlDirty => _sqlText != _savedSqlText;
    public string SqlDocumentTitle => $"{(_sqlPath is null ? "Untitled.sql" : Path.GetFileName(_sqlPath))}{(IsSqlDirty ? " *" : string.Empty)}";
    public string SqlAnalysisStatus { get => _sqlAnalysisStatus; private set => Set(ref _sqlAnalysisStatus, value); }
    public string SqlText
    {
        get => _sqlText;
        set
        {
            if (!Set(ref _sqlText, value)) return;
            ClearSqlAnalysis();
            NotifySqlDocument();
        }
    }
    public bool SqlBackslashEscapes
    {
        get => _sqlBackslashEscapes;
        set { if (Set(ref _sqlBackslashEscapes, value)) ClearSqlAnalysis(); }
    }
    public bool SqlAnsiQuotes
    {
        get => _sqlAnsiQuotes;
        set { if (Set(ref _sqlAnsiQuotes, value)) ClearSqlAnalysis(); }
    }
    public SqlStatementSpan? SelectedSqlStatement
    {
        get => _selectedSqlStatement;
        set
        {
            if (Set(ref _selectedSqlStatement, value) && value is not null) SqlSelectionRequested?.Invoke(value);
        }
    }

    public void NewSqlDocument()
    {
        if (!CanUseSqlEditor) return;
        ReplaceSqlDocument(string.Empty, null);
    }

    public Task OpenSqlFileAsync(string path)
    {
        if (!CanUseSqlEditor) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            string text = await SqlScriptFiles.ReadAsync(path);
            ReplaceSqlDocument(text, path);
            Status = "Opened SQL document.";
        });
    }

    public async Task<bool> SaveSqlFileAsync(string path)
    {
        if (!CanUseSqlEditor) return false;
        bool saved = false;
        string snapshot = SqlText;
        await RunAsync(async () =>
        {
            await SqlScriptFiles.WriteAsync(path, snapshot);
            _savedSqlText = snapshot;
            _sqlPath = path;
            NotifySqlDocument();
            Status = "Saved SQL document.";
            saved = true;
        });
        return saved;
    }

    private void ReplaceSqlDocument(string text, string? path)
    {
        SqlText = text;
        _savedSqlText = text;
        _sqlPath = path;
        SqlCaretUtf16 = 0;
        ClearSqlAnalysis();
        ClearSqlResult(false);
        NotifySqlDocument();
        SelectedWorkspaceTab = 4;
    }

    private void NotifySqlDocument()
    {
        Changed(nameof(IsSqlDirty));
        Changed(nameof(SqlDocumentTitle));
        Changed(nameof(SqlPath));
        RefreshCommands();
    }

    private void ClearSqlAnalysis()
    {
        SelectedSqlStatement = null;
        SqlStatements.Clear();
        SqlAnalysisStatus = "Analyze to refresh statement boundaries. Analysis is offline; Run SELECT uses the active connection.";
    }

    private async Task AnalyzeSqlAsync(bool selectCurrent)
    {
        SqlStatementSpan? current = null;
        await RunAsync(async () =>
        {
            ClearSqlAnalysis();
            var analysis = await _core.AnalyzeSqlScriptAsync(SqlText, SqlCaretUtf16, SqlBackslashEscapes, SqlAnsiQuotes);
            foreach (var statement in analysis.Statements) SqlStatements.Add(statement);
            SqlAnalysisStatus = $"{SqlStatements.Count} statements · {analysis.DelimiterCount} DELIMITER directives · boundaries only, no syntax validation or execution";
            if (selectCurrent && analysis.CurrentIndex is { } index) current = SqlStatements[index];
        });
        if (current is not null) SelectedSqlStatement = current;
    }
}
