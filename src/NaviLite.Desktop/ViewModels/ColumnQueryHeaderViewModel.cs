using System.Collections.ObjectModel;
using System.ComponentModel;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed class ColumnQueryHeaderViewModel : INotifyPropertyChanged, IDisposable
{
    public ColumnQueryHeaderViewModel(MainViewModel owner, PageColumn column)
    {
        Owner = owner;
        Name = column.Name;
        SqlType = column.SqlType;
        owner.PropertyChanged += OwnerChanged;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public MainViewModel Owner { get; }
    public string Name { get; }
    public string SqlType { get; }
    public ObservableCollection<FilterDraft> Filters { get; } = [];
    public bool CanEdit => Owner.CanQuery;
    public bool CanAddFilter => CanEdit && (Owner.DraftFilters.Count < 16 || Owner.DraftFilters.Any(filter => filter.Column.Length == 0));
    public bool CanQuickEdit => CanEdit && (Filters.Count > 0 || CanAddFilter);
    public bool QuickInputEnabled => Owner.Page is not null && (Filters.Count > 0 || Owner.DraftFilters.Count < 16 || Owner.DraftFilters.Any(filter => filter.Column.Length == 0));
    public bool HasFilters => Filters.Count > 0 || Owner.AppliedFilters(Name).Count > 0;
    public bool IsFilterPending => !Filters.Select(filter => filter.Snapshot()).SequenceEqual(Owner.AppliedFilters(Name));
    public string FilterCount => Filters.Count > 1 ? Filters.Count.ToString() : "";
    public string SortLabel => Owner.AppliedSort(Name) is { } sort ? $"{(sort.Direction == "desc" ? "↓" : "↑")}{Owner.SortPriority(Name)}" : "↕";
    public bool HasSort => Owner.AppliedSort(Name) is not null;
    public bool QuickReadOnly => !CanEdit || Filters.Count > 1 || (Filters.Count == 1 && Filters[0].Snapshot().Value is null);
    public string QuickValue
    {
        get => Filters.Count == 1 && Filters[0].Snapshot().Value is not null ? Filters[0].Value : "";
        set => Owner.SetQuickColumnFilter(Name, value ?? "");
    }
    public string QuickHint => Filters.Count > 1 ? $"{Filters.Count} conditions…" : Filters.Count == 1
        ? Filters[0].Snapshot().Value is null ? OperatorName : "Empty string" : "Filter…";
    public string OperatorName => Filters.Count == 1 ? Owner.FilterOperators.First(value => value.Code == Filters[0].Operator).Label : "Choose filter conditions";
    public string OperatorLabel => Filters.Count != 1 ? Filters.Count == 0 ? "=" : "…" : Filters[0].Operator switch
    {
        "notEquals" => "≠", "contains" => "∋", "startsWith" => "^", "greaterThan" => ">", "lessThan" => "<", "isNull" => "∅", "isNotNull" => "!∅", _ => "="
    };

    private void OwnerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.QueryDraftMessage) or nameof(MainViewModel.QuerySummary) or nameof(MainViewModel.CanQuery)) Refresh();
    }

    public void Refresh()
    {
        var filters = Owner.DraftFilters.Where(filter => filter.Column == Name).ToArray();
        // Retain row controls/focus while a condition's value changes.
        if (!Filters.SequenceEqual(filters))
        {
            Filters.Clear();
            foreach (var filter in filters) Filters.Add(filter);
        }
        PropertyChanged?.Invoke(this, new(string.Empty));
    }

    public override string ToString() => Name;
    public void Dispose() => Owner.PropertyChanged -= OwnerChanged;
}
