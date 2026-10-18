using System.Collections.ObjectModel;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private PageQuery _appliedQuery = new();
    private string _matchMode = "all";
    private NativeRow? _selectedRecord;
    private int _dataViewIndex;

    public ObservableCollection<ColumnOption> QueryColumns { get; } = [new("", "None")];
    public ObservableCollection<FilterDraft> DraftFilters { get; } = [];
    public ObservableCollection<SortDraft> DraftSorts { get; } = [];
    public ObservableCollection<FormField> FormFields { get; } = [];
    public IReadOnlyList<FilterOperator> FilterOperators { get; } =
    [
        new("equals", "Equals"), new("notEquals", "Not equal"), new("contains", "Contains"),
        new("startsWith", "Starts with"), new("greaterThan", "Greater than"), new("lessThan", "Less than"),
        new("isNull", "IS NULL", false), new("isNotNull", "IS NOT NULL", false)
    ];
    public MatchOption[] MatchModes { get; } = [new("all", "Match all conditions (AND)"), new("any", "Match any condition (OR)")];
    public string MatchMode { get => _matchMode; set { Set(ref _matchMode, value ?? "all"); NotifyQueryDraft(); } }
    public AsyncCommand AddFilterCommand { get; }
    public AsyncCommand AddSortCommand { get; }
    public AsyncCommand ApplyQueryCommand { get; }
    public AsyncCommand ResetQueryCommand { get; }
    public AsyncCommand PreviousRecordCommand { get; }
    public AsyncCommand NextRecordCommand { get; }
    public AsyncCommand ToggleFormCommand { get; }
    public bool CanQuery => CanBrowse && Page is not null;
    public string QuerySummary =>
        (_appliedQuery.Filters.Count > 0 ? $"Filters: {_appliedQuery.Filters.Count} · match {_appliedQuery.MatchMode}" : "All rows") +
        (_appliedQuery.Sorts.Count > 0 ? " · Sort: " + string.Join(", ", _appliedQuery.Sorts.Select(sort => $"{sort.Column} {sort.Direction.ToUpperInvariant()}")) : " · Default order");
    public string QueryDraftMessage => QueryFromControls().Matches(_appliedQuery) ? "" : "Changes pending. Choose Apply to refresh the data.";
    public PageSort? AppliedSort(string column) => _appliedQuery.Sorts.FirstOrDefault(sort => sort.Column == column);
    public int SortPriority(string column)
    {
        for (int index = 0; index < _appliedQuery.Sorts.Count; index++)
            if (_appliedQuery.Sorts[index].Column == column) return index + 1;
        return 0;
    }

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

    private PageQuery QueryFromControls()
    {
        var filters = DraftFilters.Where(filter => filter.Column.Length > 0).Select(filter => filter.Snapshot()).ToArray();
        var sorts = DraftSorts.Where(sort => sort.Column.Length > 0).Select(sort => sort.Snapshot()).ToArray();
        return new(filters, sorts, filters.Length < 2 ? "all" : MatchMode);
    }

    private void NotifyQueryDraft()
    {
        Changed(nameof(QueryDraftMessage));
        RefreshCommands();
    }

    private void SetAppliedQuery(PageQuery query)
    {
        _appliedQuery = query;
        Changed(nameof(QuerySummary));
        Changed(nameof(QueryDraftMessage));
    }

    private void ResetQueryState()
    {
        MatchMode = "all";
        DraftFilters.Clear();
        DraftFilters.Add(NewFilter());
        SetSortDrafts([]);
        SetAppliedQuery(new());
    }

    private void UpdatePageControls()
    {
        var names = Page?.Columns.Select(column => column.Name).ToArray() ?? [];
        if (!QueryColumns.Skip(1).Select(column => column.Name).SequenceEqual(names))
        {
            var filters = DraftFilters.Select(row => (Row: row, Column: row.Column)).ToArray();
            var sorts = DraftSorts.Select(row => (Row: row, Column: row.Column)).ToArray();
            QueryColumns.Clear();
            QueryColumns.Add(new("", "None"));
            foreach (string name in names) QueryColumns.Add(new(name, name));
            foreach (var entry in filters) entry.Row.Column = names.Contains(entry.Column) ? entry.Column : "";
            foreach (var entry in sorts) entry.Row.Column = names.Contains(entry.Column) ? entry.Column : "";
        }
        SelectedRecord = Page?.Rows.FirstOrDefault();
        if (SelectedRecord is null) RebuildFormFields();
        Changed(nameof(CanQuery));
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
        PageSort? sort = AppliedSort(column) is { } previous
            ? previous.Direction == "asc" ? new(column, "desc") : null
            : new(column, "asc");
        PageSort[] sorts = sort is null ? [] : [sort];
        return RunAsync(async () =>
        {
            await QueryAtStartAsync(_appliedQuery with { Sorts = sorts });
            SetSortDrafts(sorts);
        });
    }

    private FilterDraft NewFilter() => new(QueryColumns, FilterOperators, NotifyQueryDraft);
    private SortDraft NewSort() => new(QueryColumns, RefreshSortPositions);
    private void SetSortDrafts(IReadOnlyList<PageSort> sorts)
    {
        DraftSorts.Clear();
        foreach (var sort in sorts) DraftSorts.Add(new(QueryColumns, RefreshSortPositions) { Column = sort.Column, Direction = sort.Direction });
        if (DraftSorts.Count == 0) DraftSorts.Add(NewSort());
        RefreshSortPositions();
    }
    private void RefreshSortPositions()
    {
        int priority = 0;
        for (int index = 0; index < DraftSorts.Count; index++)
            DraftSorts[index].SetPosition(index, DraftSorts.Count, DraftSorts[index].Column.Length == 0 ? 0 : ++priority);
        NotifyQueryDraft();
    }
    private Task AddFilterAsync()
    {
        if (DraftFilters.Count < 16) DraftFilters.Add(NewFilter());
        NotifyQueryDraft();
        return Task.CompletedTask;
    }
    private Task AddSortAsync()
    {
        if (DraftSorts.Count < 8) DraftSorts.Add(NewSort());
        RefreshSortPositions();
        return Task.CompletedTask;
    }
    public void RemoveFilter(FilterDraft filter)
    {
        if (CanQuery && DraftFilters.Remove(filter)) NotifyQueryDraft();
    }
    public void RemoveSort(SortDraft sort)
    {
        if (CanQuery && DraftSorts.Remove(sort)) RefreshSortPositions();
    }
    public void MoveSort(SortDraft sort, int delta)
    {
        int index = DraftSorts.IndexOf(sort), next = index + delta;
        if (!CanQuery || index < 0 || next < 0 || next >= DraftSorts.Count) return;
        DraftSorts.Move(index, next);
        RefreshSortPositions();
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
