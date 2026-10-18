using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public event Action<CellInspection>? CellInspectionRequested;

    public Task InspectCellAsync(NativeRow row, string columnName)
    {
        var page = Page;
        if (!CanQuery || page is null || !row.BelongsTo(page)) return Task.CompletedTask;
        int column = page.Columns.ToList().FindIndex(value => value.Name == columnName);
        if (column < 0) return Task.CompletedTask;
        string title = $"{_openDatabase}.{_openTable} / {columnName} / {row.Label}";
        string sqlType = page.Columns[column].SqlType;
        return RunAsync(async () =>
        {
            var value = page.PreviewValue(row.Index, column);
            string source = "Complete value from the current data page; no additional query.";
            if (value.IsTruncated)
            {
                Status = $"Loading {columnName} by primary key...";
                try
                {
                    value = await _core.ReadCellAsync(page, row.Index, column);
                    source = "Current server value, located by the primary key captured with this page. The server value may have changed since the page was loaded.";
                }
                catch (InvalidOperationException error)
                {
                    source = $"Page preview only. {error.Message}";
                }
            }
            Status = value.IsTruncated ? "Cell preview opened; the value is shortened." : "Cell value opened.";
            CellInspectionRequested?.Invoke(new(title, sqlType, value, source));
        });
    }
}
