using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private SchemaNode? _selectedNode;
    private NativePage? _page;
    private readonly Stack<ulong> _pageHistory = [];
    private string _objectFilter = "";
    private string _openDatabase = "";
    private string _openTable = "";
    private string _objectTitle = "Data explorer";
    private string _ddl = "Open a table or view to inspect its CREATE statement.";
    private int _selectedWorkspaceTab;

    public AsyncCommand OpenTableCommand { get; }
    public AsyncCommand NextPageCommand { get; }
    public AsyncCommand PreviousPageCommand { get; }
    public AsyncCommand ReloadPageCommand { get; }
    public bool CanBrowse => IsConnected && !IsBusy;
    public int SelectedWorkspaceTab { get => _selectedWorkspaceTab; set => Set(ref _selectedWorkspaceTab, value); }
    public SchemaNode? SelectedNode
    {
        get => _selectedNode;
        set { Set(ref _selectedNode, value); RefreshCommands(); }
    }
    public string ObjectFilter
    {
        get => _objectFilter;
        set { Set(ref _objectFilter, value); ApplyFilter(); }
    }
    public string ObjectTitle { get => _objectTitle; private set => Set(ref _objectTitle, value); }
    public string Ddl { get => _ddl; private set => Set(ref _ddl, value); }
    public NativePage? Page
    {
        get => _page;
        private set
        {
            var previous = _page;
            Set(ref _page, value);
            Changed(nameof(PageSummary));
            RefreshCommands();
            previous?.Dispose();
        }
    }
    public string PageSummary => Page is null
        ? "Expand a database, then double-click a table or view to browse its data."
        : $"{Page.Rows.Count} rows · offset {Page.Offset:N0} · {Page.ByteLength / 1024.0:N1} KiB · {Page.TruncatedCells} shortened cells" +
          (Page.HasStableOrder ? " · ordered by primary key" : " · no primary key; page order may change");

    private void ApplyFilter()
    {
        foreach (var node in Databases) node.Filter(ObjectFilter.Trim());
    }

    public Task ExpandNodeAsync(SchemaNode node)
    {
        if (!CanBrowse || !node.IsCategory || node.IsLoaded) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            Status = $"Loading {node.Database} / {node.Name}...";
            var objects = await _core.ListObjectsAsync(_handle, node);
            node.Load(objects);
            ApplyFilter();
            Status = $"Loaded {objects.Count} {node.Name.ToLowerInvariant()} in {node.Database}.";
            Log(Status);
        });
    }

    public Task OpenSelectedAsync()
    {
        var node = SelectedNode;
        if (!CanBrowse || node?.CanOpen != true) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            Status = $"Loading {node.Database}.{node.Name}...";
            var page = await _core.ReadPageAsync(_handle, node.Database, node.Name, 0);
            _openDatabase = node.Database;
            _openTable = node.Name;
            _pageHistory.Clear();
            ObjectTitle = $"{node.Database} / {node.Name}";
            Ddl = "Loading object definition...";
            Page = page;
            SelectedWorkspaceTab = 1;
            try { Ddl = await _core.GetDdlAsync(_handle, node.Database, node.Name); }
            catch (Exception error) { Ddl = $"Object definition unavailable: {error.Message}"; Log(Ddl); }
            Status = PageSummary;
            Log($"Opened {node.Database}.{node.Name}: {page.Rows.Count} rows.");
        });
    }

    private async Task NextPageAsync()
    {
        var previous = Page;
        if (previous is null || !previous.HasMore) return;
        Status = $"Loading rows from offset {previous.NextOffset:N0}...";
        var next = await _core.ReadPageAsync(_handle, _openDatabase, _openTable, previous.NextOffset);
        _pageHistory.Push(previous.Offset);
        Page = next;
        Status = PageSummary;
    }

    private async Task PreviousPageAsync()
    {
        if (_pageHistory.Count == 0) return;
        ulong offset = _pageHistory.Peek();
        Status = $"Loading rows from offset {offset:N0}...";
        var previous = await _core.ReadPageAsync(_handle, _openDatabase, _openTable, offset);
        _pageHistory.Pop();
        Page = previous;
        Status = PageSummary;
    }

    private async Task ReloadPageAsync()
    {
        if (Page is null) return;
        Status = $"Refreshing {_openDatabase}.{_openTable}...";
        Page = await _core.ReadPageAsync(_handle, _openDatabase, _openTable, Page.Offset);
        Status = PageSummary;
    }

    private void ClearPreview()
    {
        _pageHistory.Clear();
        Page = null;
        _openDatabase = _openTable = "";
        ObjectTitle = "Data explorer";
        Ddl = "Open a table or view to inspect its CREATE statement.";
    }
}
