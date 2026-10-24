namespace Youthnic.LookupApi.Services;

/// <summary>Read-only snapshot of database metadata discovered at startup. Never guessed.</summary>
public sealed class SchemaCatalog
{
    public List<TableInfo> Tables { get; } = [];

    public IReadOnlyList<string> Schemas
        => Tables.Select(t => t.Schema).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();

    public string Summarize()
    {
        var parts = Schemas.Select(s =>
        {
            var count = Tables.Count(t => string.Equals(t.Schema, s, StringComparison.OrdinalIgnoreCase));
            return $"{s} ({count})";
        });
        return $"{Tables.Count} tables across {Schemas.Count} schema(s): {string.Join(", ", parts)}";
    }
}

public sealed record TableInfo(string Schema, string Name)
{
    public List<string> Columns { get; } = [];

    /// <summary>information_schema data_type per column name (e.g. "jsonb", "character varying"), when discovery captured it.</summary>
    public Dictionary<string, string> ColumnTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the named column exists and is a JSON/JSONB column, or a text column storing JSON (e.g. items_json).</summary>
    public bool IsJsonColumn(string columnName)
        => ColumnTypes.TryGetValue(columnName, out var dataType)
        && (dataType.Equals("json", StringComparison.OrdinalIgnoreCase)
            || dataType.Equals("jsonb", StringComparison.OrdinalIgnoreCase)
            || ((dataType.Equals("text", StringComparison.OrdinalIgnoreCase) || dataType.StartsWith("char", StringComparison.OrdinalIgnoreCase))
                && (columnName.EndsWith("_json", StringComparison.OrdinalIgnoreCase) || columnName.StartsWith("json_", StringComparison.OrdinalIgnoreCase))));

    /// <summary>Identifier as used in SQL: schema-qualified unless it is the default "public" schema.</summary>
    public string SqlIdentifier => string.Equals(Schema, "public", StringComparison.OrdinalIgnoreCase) ? Name : $"{Schema}.{Name}";
}
