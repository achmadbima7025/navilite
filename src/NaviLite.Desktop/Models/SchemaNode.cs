using System.Collections.ObjectModel;
using System.ComponentModel;

namespace NaviLite.Desktop.Models;

public sealed record SchemaObject(string Name, string Kind, string Detail);

public sealed class SchemaNode(string name, string kind, string database, string table = "", string detail = "") : INotifyPropertyChanged
{
    private bool _isVisible = true;
    private bool _isExpanded;
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Name { get; } = name;
    public string Kind { get; } = kind;
    public string Database { get; } = database;
    public string Table { get; } = table;
    public string Detail { get; } = detail;
    public string Label => IsCategory && IsLoaded ? $"{Name} ({Children.Count})" : Name;
    public string Icon => Kind switch { "database" => "▤", "table" => "▦", "view" => "◈", "column" => "·", "index" => "⚿", "foreignKey" => "↗", _ => "▸" };
    public bool IsCategory => Kind is "tables" or "views" or "procedures" or "functions" or "triggers" or "events" or "columns" or "indexes" or "foreignKeys";
    public bool CanOpen => Kind is "table" or "view";
    public bool CanInspectDefinition => Kind is "database" or "table" or "view" or "procedure" or "function" or "trigger" or "event";
    public bool IsLoaded { get; private set; }
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; PropertyChanged?.Invoke(this, new(nameof(IsExpanded))); }
    }
    public bool IsVisible
    {
        get => _isVisible;
        private set { _isVisible = value; PropertyChanged?.Invoke(this, new(nameof(IsVisible))); }
    }
    public ObservableCollection<SchemaNode> Children { get; } = [];

    public static SchemaNode DatabaseNode(string database)
    {
        var node = new SchemaNode(database, "database", database) { IsLoaded = true };
        foreach (var (name, kind) in new[] { ("Tables", "tables"), ("Views", "views"), ("Procedures", "procedures"), ("Functions", "functions"), ("Triggers", "triggers"), ("Events", "events") })
            node.Children.Add(Category(name, kind, database));
        return node;
    }

    private static SchemaNode Category(string name, string kind, string database, string table = "")
    {
        var node = new SchemaNode(name, kind, database, table);
        node.Children.Add(new("Expand to load", "placeholder", database));
        return node;
    }

    public void Load(IEnumerable<SchemaObject> objects)
    {
        Children.Clear();
        foreach (var value in objects)
        {
            var node = new SchemaNode(value.Name, value.Kind, Database, value.Kind is "table" or "view" ? value.Name : Table, value.Detail) { IsLoaded = true };
            if (node.CanOpen)
            {
                node.Children.Add(Category("Columns", "columns", Database, value.Name));
                if (node.Kind == "table")
                {
                    node.Children.Add(Category("Indexes", "indexes", Database, value.Name));
                    node.Children.Add(Category("Foreign keys", "foreignKeys", Database, value.Name));
                }
            }
            Children.Add(node);
        }
        IsLoaded = true;
        PropertyChanged?.Invoke(this, new(nameof(Label)));
    }

    public bool Filter(string filter, bool inheritedMatch = false)
    {
        bool match = inheritedMatch || string.IsNullOrWhiteSpace(filter) || Name.Contains(filter, StringComparison.OrdinalIgnoreCase);
        bool descendant = false;
        foreach (var child in Children) descendant |= child.Filter(filter, match);
        // Keep unloaded branches discoverable. Filtering never silently issues server queries.
        bool unloaded = (IsCategory && !IsLoaded) ||
            (Kind == "database" && Children.Any(child => child.IsCategory && !child.IsLoaded));
        IsVisible = match || descendant || unloaded;
        // Availability of an unloaded branch is not a name match for its ancestors.
        return match || descendant;
    }
}
