namespace NaviLite.Desktop.Models;

public sealed record PageFilter(string Column, string Operator, string? Value);
public sealed record PageSort(string Column, string Direction);
public sealed record PageQuery(IReadOnlyList<PageFilter> Filters, IReadOnlyList<PageSort> Sorts, string MatchMode = "all")
{
    public PageQuery() : this([], []) { }
    public bool Matches(PageQuery other) => MatchMode == other.MatchMode && Filters.SequenceEqual(other.Filters) && Sorts.SequenceEqual(other.Sorts);
}
public sealed record ColumnOption(string Name, string Label);
public sealed record FilterOperator(string Code, string Label, bool NeedsValue = true);
public sealed record MatchOption(string Code, string Label);
public sealed record NativeCell(string Text, bool IsNull = false, bool IsBinary = false, bool IsTruncated = false);
public sealed record FormField(string Name, string SqlType, string Text, bool IsNull, bool IsBinary, bool IsTruncated);
