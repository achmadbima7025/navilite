using System.ComponentModel;

namespace NaviLite.Desktop.Models;

public sealed class SqlResultSnapshot(ulong sequence, NativePage page, string context, long elapsedMs, int? statementIndex) : INotifyPropertyChanged, IDisposable
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public NativePage Page { get; } = page;
    public bool IsPinned { get; private set; }
    public string Title => $"{(IsPinned ? "📌 " : string.Empty)}#{sequence} · {Page.Rows.Count:N0} rows";
    public string Summary => $"Snapshot · {context}{(statementIndex is { } index ? $" · statement {index}" : string.Empty)} · {elapsedMs:N0} ms · {Page.ByteLength / 1024.0:N1} KiB · {Page.TruncatedCells:N0} shortened cells" +
        (Page.HasMore ? " · more rows available; narrow the query or change its LIMIT" : string.Empty);

    internal void TogglePin()
    {
        IsPinned = !IsPinned;
        PropertyChanged?.Invoke(this, new(nameof(IsPinned)));
        PropertyChanged?.Invoke(this, new(nameof(Title)));
    }

    public void Dispose() => Page.Dispose();
}
