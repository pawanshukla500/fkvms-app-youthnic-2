using Npgsql;
using Youthnic.LookupApi.Models;
using Youthnic.LookupApi.Options;
using Youthnic.LookupApi.Sql;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Executes the single-round-trip read-only lookup. Defense in depth:
/// the SQL runs inside "BEGIN READ ONLY; ... COMMIT;", the session default is read-only
/// via the connection string, and all values are $1/$2 parameters. Timeouts surface as
/// lookup_error (never a hang); not_found and lookup_error stay distinct.
/// Enforces marketplace seller isolation:
/// - Flipkart: queries strictly 'ETHNIC JUNCTION - Flipkart'.
/// - Myntra: queries across both 'ETHNIC JUNCTION - Myntra Youthnic' and 'VB EXPORT - Myntra PPMP'.
/// - Cross-marketplace scans return 'wrong_marketplace' with warning details.
/// </summary>
public sealed class LookupService(NpgsqlDataSource dataSource, LookupApiOptions options, LookupCache cache, ILogger<LookupService> logger)
{
    private const string NotFoundHint = "Order may not be synced yet (Forward Scan copies every ~5 minutes)";

    public Task<LookupResponse> LookupAsync(ForwardScanMapping mapping, string marketplace, string trackingId, CancellationToken ct)
        => LookupAsync(mapping, marketplace, trackingId, seller: null, ct);

    /// <param name="seller">
    /// Optional exact seller-account filter (e.g. "ETHNIC JUNCTION - Flipkart"). Used to
    /// disambiguate duplicate tracking IDs across seller accounts on shared desks.
    /// When omitted, default marketplace seller mappings apply.
    /// </param>
    public async Task<LookupResponse> LookupAsync(ForwardScanMapping mapping, string marketplace, string trackingId, string? seller, CancellationToken ct)
    {
        string[]? sellerFilters = null;
        if (!string.IsNullOrWhiteSpace(seller))
        {
            sellerFilters = [seller.Trim().ToUpperInvariant()];
        }
        else if (marketplace.Equals("flipkart", StringComparison.OrdinalIgnoreCase))
        {
            sellerFilters = ["ETHNIC JUNCTION - FLIPKART"];
        }
        else if (marketplace.Equals("myntra", StringComparison.OrdinalIgnoreCase))
        {
            sellerFilters = ["ETHNIC JUNCTION - MYNTRA YOUTHNIC", "VB EXPORT - MYNTRA PPMP"];
        }

        var sellerCacheKey = sellerFilters is { Length: > 0 } ? string.Join("+", sellerFilters) : null;
        var key = LookupCache.KeyFor(marketplace, trackingId, sellerCacheKey);
        if (cache.TryGet(key, out var cached))
        {
            logger.LogDebug("lookup cache hit {Marketplace}/{TrackingId}/{Seller}", marketplace, trackingId, sellerCacheKey ?? "-");
            return cached;
        }

        var sql = LookupQueryBuilder.Build(mapping, includeSellerFilter: sellerFilters is not null && sellerFilters.Length > 0, out var usesMarketplace, out var usesSeller);
        // Parameterized queries use the PostgreSQL extended query protocol (Parse/Bind/Execute),
        // which rejects multiple commands separated by semicolons with error 42601.
        // Strip the BEGIN/COMMIT transaction wrapper; read-only is already enforced on the connection and session.
        var querySql = sql;
        if (querySql.StartsWith("BEGIN READ ONLY;", StringComparison.OrdinalIgnoreCase))
            querySql = querySql["BEGIN READ ONLY;".Length..].Trim();
        if (querySql.EndsWith("; COMMIT;", StringComparison.OrdinalIgnoreCase))
            querySql = querySql[..^"; COMMIT;".Length].Trim();
        else if (querySql.EndsWith("COMMIT;", StringComparison.OrdinalIgnoreCase))
            querySql = querySql[..^"COMMIT;".Length].Trim();

        var items = new List<LookupItem>();
        var index = new Dictionary<(string Sku, string? Seller), int>();

        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = querySql;
        cmd.CommandTimeout = Math.Max(1, (options.QueryTimeoutMs + 999) / 1000);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(options.QueryTimeoutMs);

        try
        {
            cmd.Parameters.Add(new NpgsqlParameter { Value = trackingId });
            if (usesMarketplace)
                cmd.Parameters.Add(new NpgsqlParameter { Value = marketplace });
            if (usesSeller)
                cmd.Parameters.Add(new NpgsqlParameter { Value = sellerFilters! });

            await using var reader = await cmd.ExecuteReaderAsync(timeoutCts.Token);
            while (await reader.ReadAsync(timeoutCts.Token))
            {
                var sku = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var skuName = reader.IsDBNull(1) ? null : reader.GetString(1);
                var imageUrl = reader.IsDBNull(2) ? null : reader.GetString(2);
                var sellerValue = reader.IsDBNull(3) ? null : reader.GetString(3);
                // The items-JSON shape returns a per-row qty column (index 4); the
                // items-table shape has no such column and counts one row as one unit.
                var qty = reader.FieldCount > 4 && !reader.IsDBNull(4) ? Convert.ToInt32(reader.GetValue(4)) : 1;
                if (sku.Length == 0) continue;

                if (index.TryGetValue((sku, sellerValue), out var i))
                {
                    var existing = items[i];
                    items[i] = existing with
                    {
                        Quantity = existing.Quantity + qty,
                        ImageUrls = imageUrl is { Length: > 0 } && !existing.ImageUrls.Contains(imageUrl)
                            ? [.. existing.ImageUrls, imageUrl]
                            : existing.ImageUrls,
                    };
                }
                else
                {
                    index[(sku, sellerValue)] = items.Count;
                    items.Add(new LookupItem
                    {
                        Sku = sku,
                        SkuName = skuName ?? sku,
                        ImageUrls = imageUrl is { Length: > 0 } ? [imageUrl] : [],
                        Seller = sellerValue,
                        Quantity = qty,
                    });
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogDebug("lookup timeout for {Marketplace}/{TrackingId} after {Timeout} ms", marketplace, trackingId, options.QueryTimeoutMs);
            return Error(trackingId, marketplace, "database timeout");
        }
        catch (NpgsqlException ex) when (ex.InnerException is TimeoutException)
        {
            logger.LogDebug(ex, "lookup command timeout for {Marketplace}/{TrackingId}", marketplace, trackingId);
            return Error(trackingId, marketplace, "database timeout");
        }
        catch (NpgsqlException ex)
        {
            logger.LogError(ex, "lookup database error for {Marketplace}/{TrackingId}", marketplace, trackingId);
            return Error(trackingId, marketplace, "database error");
        }
        catch (TimeoutException ex)
        {
            logger.LogDebug(ex, "lookup timeout for {Marketplace}/{TrackingId}", marketplace, trackingId);
            return Error(trackingId, marketplace, "database timeout");
        }

        if (items.Count == 0)
        {
            // Cross-marketplace detection: check if this tracking ID exists under another seller channel
            try
            {
                var crossCheckSql = LookupQueryBuilder.BuildCrossCheckQuery(mapping);
                await using var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = crossCheckSql;
                checkCmd.CommandTimeout = Math.Max(1, (options.QueryTimeoutMs + 999) / 1000);
                checkCmd.Parameters.Add(new NpgsqlParameter { Value = trackingId });
                var channelObj = await checkCmd.ExecuteScalarAsync(timeoutCts.Token);
                if (channelObj is string actualChannel && !string.IsNullOrWhiteSpace(actualChannel))
                {
                    var chanUpper = actualChannel.ToUpperInvariant();
                    bool isMyntraOrder = chanUpper.Contains("MYNTRA");
                    bool isFlipkartOrder = chanUpper.Contains("FLIPKART");

                    if (marketplace.Equals("flipkart", StringComparison.OrdinalIgnoreCase) && isMyntraOrder)
                    {
                        var wrongMp = new LookupResponse
                        {
                            Status = "wrong_marketplace",
                            TrackingId = trackingId,
                            Marketplace = marketplace,
                            DetectedMarketplace = "myntra",
                            DetectedSeller = actualChannel,
                            Message = $"Wrong marketplace! This barcode belongs to Myntra ({actualChannel}). Do NOT pack at Flipkart desk.",
                            Hint = "Scan this package at the Myntra Packing Desk.",
                        };
                        cache.Set(key, wrongMp);
                        return wrongMp;
                    }

                    if (marketplace.Equals("myntra", StringComparison.OrdinalIgnoreCase) && isFlipkartOrder)
                    {
                        var wrongMp = new LookupResponse
                        {
                            Status = "wrong_marketplace",
                            TrackingId = trackingId,
                            Marketplace = marketplace,
                            DetectedMarketplace = "flipkart",
                            DetectedSeller = actualChannel,
                            Message = $"Wrong marketplace! This barcode belongs to Flipkart ({actualChannel}). Do NOT pack at Myntra desk.",
                            Hint = "Scan this package at the Flipkart Packing Desk.",
                        };
                        cache.Set(key, wrongMp);
                        return wrongMp;
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "cross-check check failed for {TrackingId}", trackingId);
            }

            var notFound = new LookupResponse
            {
                Status = "not_found",
                TrackingId = trackingId,
                Marketplace = marketplace,
                PendingSync = true,
                Hint = NotFoundHint,
            };
            cache.Set(key, notFound);
            return notFound;
        }

        var ok = new LookupResponse
        {
            Status = "ok",
            TrackingId = trackingId,
            Marketplace = marketplace,
            Items = items,
            Source = "forward_scan",
        };
        cache.Set(key, ok);
        return ok;
    }

    private static LookupResponse Error(string trackingId, string marketplace, string detail)
        => new() { Status = "lookup_error", TrackingId = trackingId, Marketplace = marketplace, Detail = detail };
}
