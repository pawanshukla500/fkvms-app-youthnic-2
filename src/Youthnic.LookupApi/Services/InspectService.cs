using System.Diagnostics;
using System.Net;
using Npgsql;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Sql;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Loopback-only diagnostics that answer three deployment questions in one call:
/// 1. Is the Forward Scan database reachable and read-only?
/// 2. Which seller accounts exist (ETHNIC JUNCTION - Flipkart / - Myntra Youthnic /
///    VB EXPORT - Myntra PPMP) and does a sample tracking ID resolve to
///    SKU + quantity + image link for each?
/// 3. How fast is the image URL (HTTP timing + size), i.e. will the panel render fast?
/// Every query is read-only and parameterized; identifiers are allowlist-validated.
/// </summary>
public sealed class InspectService(
    NpgsqlDataSource dataSource,
    MappingRuntimeState mappingState,
    SchemaDiscoveryService discovery,
    LookupApiOptions options,
    ILogger<InspectService> logger)
{
    // Seller-account keywords we expect to find in Forward Scan (kept broad on purpose;
    // the response reports what actually exists, including unexpected names).
    private static readonly string[] SellerKeywords = ["ethnic", "youthnic", "vb export", "vb_export", "ppmp"];

    public async Task<object> RunAsync(CancellationToken ct)
    {
        var database = await PingAsync(ct);

        SchemaCatalog? catalog = null;
        if (database.Connected)
        {
            try { catalog = await discovery.LoadCatalogAsync(ct); }
            catch (Exception ex) { logger.LogDebug(ex, "inspect: catalog load failed"); }
        }

        var result = new Dictionary<string, object?>
        {
            ["checkedAtUtc"] = DateTimeOffset.UtcNow,
            ["database"] = database,
            ["mapping"] = mappingState.IsReady
                ? new { status = "ready", source = mappingState.Source }
                : new { status = "pending", detail = mappingState.PendingDetail },
            ["tables"] = catalog?.Summarize(),
        };

        if (catalog is not null)
            result["columns"] = catalog.Tables
                .Select(t => new { table = $"{t.Schema}.{t.Name}", columns = t.Columns })
                .ToArray();

        var mapping = mappingState.Mapping;
        if (database.Connected && mappingState.IsReady && mapping is not null && !string.IsNullOrWhiteSpace(mapping.ColSeller))
        {
            var sellers = await DistinctSellersAsync(mapping, ct);
            result["sellerAccounts"] = sellers;

            var samples = new List<object>();
            var matched = sellers
                .Where(s => SellerKeywords.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            foreach (var seller in matched)
                samples.AddRange(await SampleRowsAsync(mapping, seller, ct));
            result["samples"] = samples;

            var firstImage = samples
                .OfType<Dictionary<string, object?>>()
                .Select(s => s.TryGetValue("imageUrl", out var val) ? val as string : null)
                .FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            if (firstImage is not null)
                result["imageFetch"] = await ProbeImageAsync(firstImage, ct);
        }

        return result;
    }

    private sealed record DatabaseStatus(bool Connected, bool ReadOnly);

    private async Task<DatabaseStatus> PingAsync(CancellationToken ct)
    {
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SHOW transaction_read_only;";
            cmd.CommandTimeout = Math.Max(1, (options.QueryTimeoutMs + 999) / 1000);
            var val = (await cmd.ExecuteScalarAsync(ct))?.ToString();
            return new DatabaseStatus(true, string.Equals(val, "on", StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "inspect: database ping failed");
            return new DatabaseStatus(false, false);
        }
    }

    private async Task<List<string>> DistinctSellersAsync(ForwardScanMapping m, CancellationToken ct)
    {
        var list = new List<string>();
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = Math.Max(1, (options.QueryTimeoutMs + 999) / 1000);
            cmd.CommandText =
                "SELECT DISTINCT o." + LookupQueryBuilder.Quoted(m.ColSeller) +
                " FROM " + TableIdent(m) + " o WHERE o." + LookupQueryBuilder.Quoted(m.ColSeller) +
                " IS NOT NULL ORDER BY 1 LIMIT 300;";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                list.Add(reader.GetString(0));
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "inspect: seller listing failed");
            list.Add("<error: " + ex.GetType().Name + ">");
        }
        return list;
    }

    private async Task<List<object>> SampleRowsAsync(ForwardScanMapping m, string seller, CancellationToken ct)
    {
        var rows = new List<object>();
        try
        {
            await using var conn = await dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandTimeout = Math.Max(1, (options.QueryTimeoutMs + 999) / 1000);
            var jsonMode = !string.IsNullOrWhiteSpace(m.ItemsJsonColumn);
            cmd.CommandText = jsonMode ? BuildJsonSamplesSql(m) : BuildTableSamplesSql(m);
            cmd.Parameters.Add(new NpgsqlParameter { Value = seller });

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var imageUrl = reader.IsDBNull(3) ? null : reader.GetString(3);
                rows.Add(new Dictionary<string, object?>
                {
                    ["seller"] = seller,
                    ["trackingId"] = reader.GetString(0),
                    ["sku"] = reader.GetString(1),
                    ["skuName"] = reader.IsDBNull(2) ? null : reader.GetString(2),
                    ["imageUrl"] = imageUrl,
                    ["qty"] = Convert.ToInt32(reader.GetValue(5)),
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "inspect: sample rows failed for {Seller}", seller);
            rows.Add(new Dictionary<string, object?> { ["seller"] = seller, ["error"] = ex.GetType().Name });
        }
        return rows;
    }

    // Table mode: one row per (tracking, sku) with the item count rolled up via COUNT(*).
    private static string BuildTableSamplesSql(ForwardScanMapping m)
    {
        var hasSkuMap = !string.IsNullOrWhiteSpace(m.SkuMapTable);
        var hasImages = !string.IsNullOrWhiteSpace(m.ImageTable) &&
                        !string.IsNullOrWhiteSpace(m.ImageSkuJoinColumn) &&
                        !string.IsNullOrWhiteSpace(m.ColImageUrl);

        var sb = new System.Text.StringBuilder(512);
        sb.Append("SELECT o.").Append(LookupQueryBuilder.Quoted(m.ColTracking))
          .Append(" AS tracking, oi.").Append(LookupQueryBuilder.Quoted(m.OrderItemsSkuColumn)).Append(" AS sku, ")
          .Append(hasSkuMap ? "m." + LookupQueryBuilder.Quoted(m.ColSkuName) : "NULL").Append(" AS sku_name, ")
          .Append(hasImages ? "im." + LookupQueryBuilder.Quoted(m.ColImageUrl) : "NULL").Append(" AS image_url, o.")
          .Append(LookupQueryBuilder.Quoted(m.ColSeller)).Append(" AS seller, COUNT(*) AS qty")
          .Append(" FROM ").Append(TableIdent(m)).Append(" o JOIN ")
          .Append(LookupQueryBuilder.TableIdent(m.SchemaOrders, m.OrdersItemsTable)).Append(" oi ON oi.")
          .Append(LookupQueryBuilder.Quoted(m.OrderItemsOrderJoinColumn)).Append(" = o.")
          .Append(LookupQueryBuilder.Quoted(m.OrderItemsOrderJoinColumn));
        if (hasSkuMap)
            sb.Append(" LEFT JOIN ").Append(LookupQueryBuilder.TableIdent(m.SchemaOrders, m.SkuMapTable)).Append(" m ON m.")
              .Append(LookupQueryBuilder.Quoted(m.ColSku)).Append(" = oi.")
              .Append(LookupQueryBuilder.Quoted(m.OrderItemsSkuColumn));
        if (hasImages)
            sb.Append(" LEFT JOIN ").Append(LookupQueryBuilder.TableIdent(m.SchemaOrders, m.ImageTable)).Append(" im ON im.")
              .Append(LookupQueryBuilder.Quoted(m.ImageSkuJoinColumn)).Append(" = oi.")
              .Append(LookupQueryBuilder.Quoted(m.OrderItemsSkuColumn));
        sb.Append(" WHERE upper(o.").Append(LookupQueryBuilder.Quoted(m.ColSeller)).Append(") = upper($1)")
          .Append(" GROUP BY 1,2,3,4,5 ORDER BY 1 DESC LIMIT 3;");
        return sb.ToString();
    }

    // JSON mode: expand the items array on the orders row itself; qty parsed defensively.
    private static string BuildJsonSamplesSql(ForwardScanMapping m)
    {
        var hasSkuMap = !string.IsNullOrWhiteSpace(m.SkuMapTable) &&
                        !string.IsNullOrWhiteSpace(m.ColSku) &&
                        !string.IsNullOrWhiteSpace(m.ColSkuName);
        var hasImages = !string.IsNullOrWhiteSpace(m.ImageTable) &&
                        !string.IsNullOrWhiteSpace(m.ImageSkuJoinColumn) &&
                        !string.IsNullOrWhiteSpace(m.ColImageUrl);

        var sb = new System.Text.StringBuilder(768);
        sb.Append("SELECT o.").Append(LookupQueryBuilder.Quoted(m.ColTracking))
          .Append(" AS tracking, (it.item ->> 'sku') AS sku, ")
          .Append(hasSkuMap
              ? "COALESCE(NULLIF(it.item ->> 'title', ''), m." + LookupQueryBuilder.Quoted(m.ColSkuName) + ")"
              : "NULLIF(it.item ->> 'title', '')").Append(" AS sku_name, ")
          .Append(hasImages
              ? "COALESCE(NULLIF(it.item ->> 'image_url', ''), im." + LookupQueryBuilder.Quoted(m.ColImageUrl) + ")"
              : "NULLIF(it.item ->> 'image_url', '')").Append(" AS image_url, o.")
          .Append(LookupQueryBuilder.Quoted(m.ColSeller)).Append(" AS seller, ")
          .Append("GREATEST(CASE WHEN (it.item ->> 'qty') ~ '^[0-9]+$' THEN (it.item ->> 'qty')::int ELSE 1 END, 1) AS qty")
          .Append(" FROM ").Append(TableIdent(m)).Append(" o CROSS JOIN LATERAL jsonb_array_elements(o.")
          .Append(LookupQueryBuilder.Quoted(m.ItemsJsonColumn)).Append("::jsonb) AS it(item)");
        if (hasSkuMap)
            sb.Append(" LEFT JOIN ").Append(LookupQueryBuilder.TableIdent(m.SchemaOrders, m.SkuMapTable)).Append(" m ON m.")
              .Append(LookupQueryBuilder.Quoted(m.ColSku)).Append(" = (it.item ->> 'sku')");
        if (hasImages)
            sb.Append(" LEFT JOIN ").Append(LookupQueryBuilder.TableIdent(m.SchemaOrders, m.ImageTable)).Append(" im ON im.")
              .Append(LookupQueryBuilder.Quoted(m.ImageSkuJoinColumn)).Append(" = (it.item ->> 'sku')");
        sb.Append(" WHERE upper(o.").Append(LookupQueryBuilder.Quoted(m.ColSeller)).Append(") = upper($1)")
          .Append(" ORDER BY 1 DESC LIMIT 3;");
        return sb.ToString();
    }

    private static async Task<object> ProbeImageAsync(string url, CancellationToken ct)
    {
        using var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 65_535); // first 64 KB is enough to judge speed
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var headersMs = sw.ElapsedMilliseconds;
            var bytes = response.Content.Headers.ContentLength;
            await response.Content.LoadIntoBufferAsync();
            sw.Stop();
            return new
            {
                url = url.Length > 160 ? url[..160] + "…" : url,
                status = (int)response.StatusCode,
                contentType = response.Content.Headers.ContentType?.ToString(),
                bytes = bytes,
                headersMs,
                totalMs = sw.ElapsedMilliseconds,
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new { url = url.Length > 160 ? url[..160] + "…" : url, error = ex.GetType().Name, totalMs = sw.ElapsedMilliseconds };
        }
    }

    private static string TableIdent(ForwardScanMapping m)
        => string.IsNullOrWhiteSpace(m.SchemaOrders)
            ? LookupQueryBuilder.Quoted(m.OrdersTable)
            : LookupQueryBuilder.Quoted(m.SchemaOrders) + "." + LookupQueryBuilder.Quoted(m.OrdersTable);
}
