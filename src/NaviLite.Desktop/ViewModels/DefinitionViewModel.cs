using System.IO;
using System.Text;
using NaviLite.Desktop.Models;

namespace NaviLite.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    private ObjectDefinition? _definition;

    public event Action<string>? DefinitionCopyRequested;
    public AsyncCommand InspectDefinitionCommand { get; }
    public AsyncCommand ReloadDefinitionCommand { get; }
    public AsyncCommand CopyDefinitionCommand { get; }
    public bool CanExportDefinition => Definition is not null && !IsBusy;
    public ObjectDefinition? Definition
    {
        get => _definition;
        private set
        {
            Set(ref _definition, value);
            Changed(nameof(DefinitionTitle));
            Changed(nameof(DefinitionSummary));
            Changed(nameof(DefinitionSql));
            Changed(nameof(CanExportDefinition));
            RefreshCommands();
        }
    }
    public string DefinitionTitle => Definition is { } value ? $"{value.Database} / {value.Name}" : "Object definition";
    public string DefinitionSummary => Definition is { } value
        ? $"{value.Kind.ToUpperInvariant()} · {Encoding.UTF8.GetByteCount(value.Sql):N0} bytes UTF-8 · read only"
        : "Select a database or object in the navigator, then choose Definition. SQL is displayed exactly as returned by the server.";
    public string DefinitionSql => Definition?.Sql ?? "";

    private Task InspectSelectedDefinitionAsync()
    {
        var node = SelectedNode;
        if (!CanBrowse || node?.CanInspectDefinition != true) return Task.CompletedTask;
        return RunAsync(() => LoadDefinitionAsync(node.Database, node.Name, node.Kind));
    }

    private Task ReloadDefinitionAsync()
    {
        var definition = Definition;
        if (!CanBrowse || definition is null) return Task.CompletedTask;
        return RunAsync(() => LoadDefinitionAsync(definition.Database, definition.Name, definition.Kind));
    }

    private async Task LoadDefinitionAsync(string database, string name, string kind)
    {
        Status = $"Loading definition of {database}.{name}...";
        var definition = await _core.ReadDefinitionAsync(_handle, database, name, kind);
        Definition = definition;
        SelectedWorkspaceTab = 3;
        Status = $"Loaded {kind} definition: {database}.{name}.";
        Log(Status);
    }

    private Task CopyDefinitionAsync()
    {
        var definition = Definition;
        if (!CanExportDefinition || definition is null) return Task.CompletedTask;
        return RunAsync(() =>
        {
            if (definition.Sql.Contains('\0'))
                throw new InvalidOperationException("The definition contains NUL and cannot be copied as clipboard text. Save it as SQL instead.");
            DefinitionCopyRequested?.Invoke(definition.Sql);
            Status = $"Copied {definition.Kind} definition to the clipboard.";
            return Task.CompletedTask;
        });
    }

    public Task SaveDefinitionAsync(string path, ObjectDefinition definition)
    {
        if (!CanExportDefinition) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            await File.WriteAllTextAsync(path, definition.Sql, new UTF8Encoding(false, true));
            Status = $"Saved {definition.Kind} definition as UTF-8 SQL.";
            Log(Status);
        });
    }
}
