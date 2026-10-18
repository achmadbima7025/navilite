using System.Collections.ObjectModel;
using NaviLite.Desktop.Models;
using NaviLite.Desktop.Services;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private SqlHistoryEntry? _selectedHistory;
    private string _historyStatus = "Open a connection to record internal SQL. Parameter values are excluded.";
    public ObservableCollection<SqlHistoryEntry> SqlHistory { get; } = [];
    public AsyncCommand RefreshHistoryCommand { get; }
    public AsyncCommand ClearHistoryCommand { get; }
    public event Action<string>? RowsCopyRequested;
    public string HistoryStatus { get => _historyStatus; private set => Set(ref _historyStatus, value); }
    public SqlHistoryEntry? SelectedHistory
    {
        get => _selectedHistory;
        set { Set(ref _selectedHistory, value); Changed(nameof(HistoryStatement)); }
    }
    public string HistoryStatement => SelectedHistory is { } entry
        ? entry.Statement + (entry.Shortened ? "\n\n[Statement shortened at 16 KiB]" : "")
        : "Select a history entry to inspect its SQL template. Bound parameter values are excluded.";

    private async Task LoadHistoryAsync()
    {
        ulong handle = _handle;
        if (handle == 0) return;
        try
        {
            var entries = await _core.ReadHistoryAsync(handle);
            if (_handle != handle) return;
            ulong? selected = SelectedHistory?.Id;
            SqlHistory.Clear();
            foreach (var entry in entries) SqlHistory.Add(entry);
            SelectedHistory = entries.FirstOrDefault(entry => entry.Id == selected) ?? entries.FirstOrDefault();
            HistoryStatus = $"{entries.Count} recent statements · up to 200 per connection · parameter values excluded · memory only";
        }
        catch (Exception error) { HistoryStatus = $"SQL history unavailable: {error.Message}"; }
    }

    private void ResetHistory()
    {
        SqlHistory.Clear();
        SelectedHistory = null;
        HistoryStatus = "Internal SQL history for the active connection. Parameter values are excluded.";
    }

    private async Task ClearHistoryAsync()
    {
        await _core.ClearHistoryAsync(_handle);
        ResetHistory();
        Status = "SQL history cleared.";
    }

    public Task CopyRowsAsync(IEnumerable<NativeRow> selection, RowCopyFormat format)
    {
        var page = Page;
        if (!CanQuery || page is null) return Task.CompletedTask;
        var rows = selection.Where(row => row.BelongsTo(page)).DistinctBy(row => row.Index).OrderBy(row => row.Index).ToArray();
        if (rows.Length == 0) { Status = "Select records to copy."; return Task.CompletedTask; }
        return RunAsync(async () =>
        {
            string label = format == RowCopyFormat.SqlInsert ? "SQL INSERT" : format.ToString().ToUpperInvariant();
            Status = $"Preparing {rows.Length} records as {label}...";
            string text = format == RowCopyFormat.SqlInsert
                ? await _core.CopyRowsAsInsertAsync(page, rows)
                : await Task.Run(() => RowCopy.Format(page, rows, format));
            RowsCopyRequested?.Invoke(text);
            Status = format == RowCopyFormat.SqlInsert
                ? $"Copied {rows.Length} records as SQL INSERT. Review the target, keys, constraints, and session settings before running it."
                : $"Copied {rows.Length} records as {label}.";
            Log(Status);
        });
    }
}
