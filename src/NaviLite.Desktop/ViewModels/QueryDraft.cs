using System.ComponentModel;
using System.Runtime.CompilerServices;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public abstract class QueryDraft(Action changed) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    protected void Update(ref string field, string value, [CallerMemberName] string? name = null)
    {
        if (field == value) return;
        field = value;
        Notify(name);
        Edited();
        changed();
    }
    protected virtual void Edited() { }
}

public sealed class FilterDraft(IEnumerable<ColumnOption> columns, IReadOnlyList<FilterOperator> operators, Action changed) : QueryDraft(changed)
{
    private string _column = "", _operator = "equals", _value = "";
    public IEnumerable<ColumnOption> Columns { get; } = columns;
    public IReadOnlyList<FilterOperator> Operators { get; } = operators;
    public string Column { get => _column; set => Update(ref _column, value ?? ""); }
    public string Operator { get => _operator; set => Update(ref _operator, value ?? "equals"); }
    public string Value { get => _value; set => Update(ref _value, value ?? ""); }
    public bool ValueEnabled => Column.Length > 0 && CurrentOperator.NeedsValue;
    private FilterOperator CurrentOperator => Operators.FirstOrDefault(value => value.Code == Operator) ?? Operators[0];
    protected override void Edited() => Notify(nameof(ValueEnabled));
    public PageFilter Snapshot() => new(Column, CurrentOperator.Code, CurrentOperator.NeedsValue ? Value : null);
}

public sealed class SortDraft(IEnumerable<ColumnOption> columns, Action changed) : QueryDraft(changed)
{
    private string _column = "", _direction = "asc";
    private int _position, _count, _priority;
    public IEnumerable<ColumnOption> Columns { get; } = columns;
    public string[] Directions { get; } = ["asc", "desc"];
    public string Column { get => _column; set => Update(ref _column, value ?? ""); }
    public string Direction { get => _direction; set => Update(ref _direction, value ?? "asc"); }
    public string Priority => _priority == 0 ? "—" : _priority.ToString();
    public bool CanMoveUp => _position > 0;
    public bool CanMoveDown => _position + 1 < _count;
    public PageSort Snapshot() => new(Column, Direction);
    public void SetPosition(int position, int count, int priority)
    {
        _position = position;
        _count = count;
        _priority = priority;
        Notify(nameof(Priority));
        Notify(nameof(CanMoveUp));
        Notify(nameof(CanMoveDown));
    }
}
