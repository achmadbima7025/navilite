using System.Collections.ObjectModel;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private PageQuery _appliedQuery = new();
    private string _filterColumn = "", _filterOperatorCode = "equals", _filterValue = "", _sortColumn = "", _sortDirection = "asc";
    private NativeRow? _selectedRecord;
    private int _dataViewIndex;

    public ObservableCollection<ColumnOption> QueryColumns { get; } = [new("", "None")];
    public ObservableCollection<FormField> FormFields { get; } = [];
    public IReadOnlyList<FilterOperator> FilterOperators { get; } =
    [
        new("equals", "Equals"), new("notEquals", "Not equal"), new("contains", "Contains"),
        new("startsWith", "Starts with"), new("greaterThan", "Greater than"), new("lessThan", "Less than"),
        new("isNull", "IS NULL", false), new("isNotNull", "IS NOT NULL", false)
    ];
    public string[] SortDirections { get; } = ["asc", "desc"];
    public AsyncCommand ApplyQueryCommand { get; }
    public AsyncCommand ResetQueryCommand { get; }
    public AsyncCommand PreviousRecordCommand { get; }
    public AsyncCommand NextRecordCommand { get; }
    public AsyncCommand ToggleFormCommand { get; }
    public bool CanQuery => CanBrowse && Page is not null;
    public bool FilterValueEnabled => CanQuery && FilterColumn.Length > 0 && CurrentFilterOperator.NeedsValue;
    private FilterOperator CurrentFilterOperator => FilterOperators.FirstOrDefault(value => value.Code == FilterOperatorCode) ?? FilterOperators[0];
    public string FilterColumn { get => _filterColumn; set { Set(ref _filterColumn, value ?? ""); NotifyQueryDraft(); } }
    public string FilterOperatorCode { get => _filterOperatorCode; set { Set(ref _filterOperatorCode, value ?? "equals"); NotifyQueryDraft(); } }
    public string FilterValue { get => _filterValue; set { Set(ref _filterValue, value ?? ""); NotifyQueryDraft(); } }
    public string SortColumn { get => _sortColumn; set { Set(ref _sortColumn, value ?? ""); NotifyQueryDraft(); } }
    public string SortDirection { get => _sortDirection; set { Set(ref _sortDirection, value ?? "asc"); NotifyQueryDraft(); } }
    public string? AppliedSortColumn => _appliedQuery.Sort?.Column;
    public string? AppliedSortDirection => _appliedQuery.Sort?.Direction;
    public string QuerySummary =>
        (_appliedQuery.Filter is { } filter ? $"Filter: {filter.Column} {FilterOperators.First(value => value.Code == filter.Operator).Label}" : "All rows") +
        (_appliedQuery.Sort is { } sort ? $" · Sort: {sort.Column} {sort.Direction.ToUpperInvariant()}" : " · Default order");
    public string QueryDraftMessage => QueryFromControls() == _appliedQuery ? "" : "Changes pending. Choose Apply to refresh the data.";

    public int DataViewIndex
    {
        get => _dataViewIndex;
        set { if (Set(ref _dataViewIndex, value)) RebuildFormFields(); }
    }
    public NativeRow? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (value is not null && !value.BelongsTo(Page)) return;
            if (Set(ref _selectedRecord, value))
            {
                Changed(nameof(RecordSummary));
                RebuildFormFields();
                RefreshCommands();
            }
        }
    }
    public string RecordSummary => SelectedRecord?.Label ?? "No record selected";

    private PageQuery QueryFromControls() => new(
        FilterColumn.Length == 0 ? null : new(FilterColumn, CurrentFilterOperator.Code, CurrentFilterOperator.NeedsValue ? FilterValue : null),
        SortColumn.Length == 0 ? null : new(SortColumn, SortDirection));

    private void NotifyQueryDraft()
    {
        Changed(nameof(QueryDraftMessage));
        Changed(nameof(FilterValueEnabled));
    }

    private void SetAppliedQuery(PageQuery query)
    {
        _appliedQuery = query;
        Changed(nameof(QuerySummary));
        Changed(nameof(QueryDraftMessage));
    }

    private void ResetQueryState()
    {
        SetAppliedQuery(new());
        FilterColumn = SortColumn = FilterValue = "";
        FilterOperatorCode = "equals";
        SortDirection = "asc";
    }

    private void UpdatePageControls()
    {
        var names = Page?.Columns.Select(column => column.Name).ToArray() ?? [];
        if (!QueryColumns.Skip(1).Select(column => column.Name).SequenceEqual(names))
        {
            string filter = FilterColumn, sort = SortColumn;
            QueryColumns.Clear();
            QueryColumns.Add(new("", "None"));
            foreach (string name in names) QueryColumns.Add(new(name, name));
            FilterColumn = names.Contains(filter) ? filter : "";
            SortColumn = names.Contains(sort) ? sort : "";
        }
        SelectedRecord = Page?.Rows.FirstOrDefault();
        if (SelectedRecord is null) RebuildFormFields();
        Changed(nameof(CanQuery));
        Changed(nameof(FilterValueEnabled));
    }

    private async Task QueryAtStartAsync(PageQuery query)
    {
        Status = $"Applying filter and sort to {_openDatabase}.{_openTable}...";
        var page = await _core.ReadPageAsync(_handle, _openDatabase, _openTable, 0, query);
        SetAppliedQuery(query);
        _pageHistory.Clear();
        Page = page;
        Status = PageSummary;
        Log($"Updated filter/sort for {_openDatabase}.{_openTable}: {page.Rows.Count} rows.");
    }

    private Task ApplyQueryAsync() => QueryAtStartAsync(QueryFromControls());

    private async Task ResetQueryAsync()
    {
        await QueryAtStartAsync(new());
        ResetQueryState();
    }

    public Task SortByAsync(string column)
    {
        if (!CanQuery || !QueryColumns.Any(option => option.Name == column)) return Task.CompletedTask;
        PageSort? sort = _appliedQuery.Sort is { } previous && previous.Column == column
            ? previous.Direction == "asc" ? new(column, "desc") : null
            : new(column, "asc");
        return RunAsync(async () =>
        {
            await QueryAtStartAsync(_appliedQuery with { Sort = sort });
            SortColumn = sort?.Column ?? "";
            SortDirection = sort?.Direction ?? "asc";
        });
    }

    private void RebuildFormFields()
    {
        FormFields.Clear();
        if (DataViewIndex != 1 || SelectedRecord is null || Page is null) return;
        for (int column = 0; column < Page.Columns.Count; column++)
        {
            var metadata = Page.Columns[column];
            var cell = SelectedRecord.Cell(column);
            FormFields.Add(new(metadata.Name, metadata.SqlType, cell.Text, cell.IsNull, cell.IsBinary, cell.IsTruncated));
        }
    }

    private Task SelectRecordAsync(int delta)
    {
        if (SelectedRecord is null || Page is null) return Task.CompletedTask;
        int index = SelectedRecord.Index + delta;
        if ((uint)index < (uint)Page.Rows.Count) SelectedRecord = Page.Rows[index];
        return Task.CompletedTask;
    }
}
