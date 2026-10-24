using Npgsql;

namespace Youthnic.LookupApi.Services;

/// <summary>Lightweight "SELECT 1" ping for /health. Never throws; false means unavailable.</summary>
public sealed class DatabasePinger(NpgsqlDataSource dataSource, ILogger<DatabasePinger> logger)
{
    private const int PingTimeoutMs = 1500;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(PingTimeoutMs);
            await using var conn = await dataSource.OpenConnectionAsync(timeoutCts.Token);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1";
            cmd.CommandTimeout = 2;
            var result = await cmd.ExecuteScalarAsync(timeoutCts.Token);
            return result is not null;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller cancelled — let it propagate
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Database health ping failed");
            return false;
        }
    }
}
