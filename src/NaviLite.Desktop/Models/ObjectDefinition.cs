namespace NaviLite.Desktop.Models;

public sealed record ObjectDefinition(string Database, string Name, string Kind, string Sql, IReadOnlyList<DefinitionProperty> Properties);

public sealed record DefinitionProperty(string Name, string? Value)
{
    public string ValueText => Value ?? "NULL";
}
