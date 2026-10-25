using Npgsql;
using Youthnic.LookupApi.Options;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Startup discovery against information_schema (read-only). Loads schemas/tables/columns,
/// logs a compact summary, then resolves the mapping: if the required env mapping is fully
/// provided it is validated as-is; otherwise a conservative auto-match runs ONLY against the
/// discovered metadata. Anything still unresolved → mapping-pending mode (never guessed).
/// </summary>
public sealed class SchemaDiscoveryService(NpgsqlDataSource dataSource, ILogger<SchemaDiscoveryService> logger)
{
    private const string TablesSql =
        "SELECT table_schema, table_name FROM information_schema.tables " +
        "WHERE table_type = 'BASE TABLE' AND table_schema NOT IN ('pg_catalog','information_schema') " +
        "ORDER BY table_schema, table_name";

    private const string ColumnsSql =
        "SELECT table_schema, table_name, column_name, data_type FROM information_schema.columns " +
        "WHERE table_schema NOT IN ('pg_catalog','information_schema') " +
        "ORDER BY table_schema, table_name, ordinal_position";

    public async Task DiscoverAndResolveAsync(LookupApiOptions options, MappingRuntimeState state, CancellationToken ct = default)
    {
        SchemaCatalog? catalog = null;
        try
        {
            catalog = await LoadCatalogAsync(ct);
            logger.LogInformation("Schema discovery: {Summary}", catalog.Summarize());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Schema discovery failed (database unreachable or metadata denied)");
        }

        var mapping = options.Mapping;
        if (!string.IsNullOrWhiteSpace(mapping.ImageTable) && string.IsNullOrWhiteSpace(mapping.ImageSkuJoinColumn))
        {
            var joinCol = !string.IsNullOrWhiteSpace(mapping.ColSku) ? mapping.ColSku : "sku";
            mapping = mapping with { ImageSkuJoinColumn = joinCol };
        }

        var envMissing = MappingValidator.FindMissingRequired(mapping);
        if (envMissing.Count == 0)
        {
            try
            {
                MappingValidator.ValidateMapping(mapping);
                state.SetReady(mapping, "configured via environment");
                logger.LogInformation("Lookup mapping ready from environment: {Mapping}", Describe(mapping));
                return;
            }
            catch (InvalidOperationException ex)
            {
                state.SetPending($"configured mapping failed validation: {ex.Message}");
                logger.LogError("Configured mapping failed validation: {Detail}", state.PendingDetail);
                return;
            }
        }

        if (catalog is null)
        {
            state.SetPending($"mapping env vars incomplete ({string.Join(", ", envMissing)}) and schema discovery is unavailable (database unreachable)");
            logger.LogWarning("Mapping pending: {Detail}", state.PendingDetail);
            return;
        }

        if (AutoMatcher.TryResolve(catalog, options.Mapping, logger, out var resolved, out var detail))
        {
            state.SetReady(resolved, "auto-matched from discovered metadata");
            logger.LogInformation("Lookup mapping auto-matched: {Mapping}", Describe(resolved));
        }
        else
        {
            state.SetPending(detail);
            logger.LogWarning("Mapping pending: {Detail}", state.PendingDetail);
        }
    }

    public async Task<SchemaCatalog> LoadCatalogAsync(CancellationToken ct)
    {
        var catalog = new SchemaCatalog();
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using (var cmd = new NpgsqlCommand(TablesSql, conn) { CommandTimeout = 5 })
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                catalog.Tables.Add(new TableInfo(reader.GetString(0), reader.GetString(1)));
        }

        await using (var cmd = new NpgsqlCommand(ColumnsSql, conn) { CommandTimeout = 5 })
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                var table = catalog.Tables.FirstOrDefault(t =>
                    string.Equals(t.Schema, reader.GetString(0), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(t.Name, reader.GetString(1), StringComparison.OrdinalIgnoreCase));
                if (table is null) continue;
                var columnName = reader.GetString(2);
                table.Columns.Add(columnName);
                if (!reader.IsDBNull(3) && !table.ColumnTypes.ContainsKey(columnName))
                    table.ColumnTypes[columnName] = reader.GetString(3);
            }
        }

        return catalog;
    }

    private static string Describe(ForwardScanMapping m)
        => $"orders={TableName(m)}.{m.ColTracking}{(string.IsNullOrWhiteSpace(m.ColMarketplace) ? "" : $"/{m.ColMarketplace}")}" +
           (string.IsNullOrWhiteSpace(m.ItemsJsonColumn)
               ? $", items={m.OrdersItemsTable}({m.OrderItemsOrderJoinColumn},{m.OrderItemsSkuColumn})"
               : $", items={TableName(m)}.{m.ItemsJsonColumn} (json)") +
           $"{(string.IsNullOrWhiteSpace(m.SkuMapTable) ? "" : $", skuMap={m.SkuMapTable}({m.ColSku}->{m.ColSkuName})")}" +
           $"{(string.IsNullOrWhiteSpace(m.ImageTable) ? "" : $", images={m.ImageTable}({m.ImageSkuJoinColumn}->{m.ColImageUrl})")}" +
           $"{(string.IsNullOrWhiteSpace(m.ColSeller) ? "" : ", seller=" + m.ColSeller)}";

    private static string TableName(ForwardScanMapping m)
        => string.IsNullOrWhiteSpace(m.SchemaOrders) ? m.OrdersTable : $"{m.SchemaOrders}.{m.OrdersTable}";
}
