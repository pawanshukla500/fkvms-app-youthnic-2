using Npgsql;

namespace Youthnic.LookupApi.Services;

public sealed record ReadOnlyProbeOutcome(bool ReadOnlyVerified, string Detail);

/// <summary>Startup probe verifying the session really is read-only. Pure decision logic — testable without a database.</summary>
public static class ReadOnlyProbeDecision
{
    /// <summary>
    /// When read-only is required, start only if the probe ran and verified read-only operation.
    /// A missing/unreachable probe result is treated as "cannot guarantee" → refuse to start.
    /// </summary>
    public static bool ShouldStart(bool readOnlyRequired, ReadOnlyProbeOutcome? outcome)
        => !readOnlyRequired || (outcome is not null && outcome.ReadOnlyVerified);
}

/// <summary>
/// Runs the startup verification against the live database:
///  1. SHOW transaction_read_only must return 'on' (connection string enforces it via Options=-c default_transaction_read_only=on).
///  2. SELECT pg_catalog.set_config('default_transaction_read_only','on',false) must succeed.
///  3. Best-effort diagnostics (pg_is_in_recovery, role) are logged but never fail the probe.
/// </summary>
public sealed class ReadOnlyProbeRunner(NpgsqlDataSource dataSource, ILogger logger)
{
    public async Task<ReadOnlyProbeOutcome> RunAsync(CancellationToken ct = default)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandTimeout = 5;

        cmd.CommandText = "SHOW transaction_read_only";
        var value = (string?)await cmd.ExecuteScalarAsync(ct);
        if (!string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
            return new ReadOnlyProbeOutcome(false, $"transaction_read_only='{value}' (expected 'on')");

        cmd.CommandText = "SELECT pg_catalog.set_config('default_transaction_read_only','on',false)";
        var configured = (string?)await cmd.ExecuteScalarAsync(ct);
        if (!string.Equals(configured, "on", StringComparison.OrdinalIgnoreCase))
            return new ReadOnlyProbeOutcome(false, "could not enforce default_transaction_read_only=on for the session");

        // Best-effort diagnostics — logged, never a failure reason.
        try
        {
            cmd.CommandText = "SELECT pg_is_in_recovery(), current_user, inet_server_addr()";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                var standby = reader.GetBoolean(0);
                var role = reader.GetString(1);
                logger.LogInformation("Database diagnostics: in_hot_standby={Standby}, role={Role}, server={Server}",
                    standby, role, reader.IsDBNull(2) ? "unknown" : reader.GetValue(2));
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Best-effort read-only diagnostics unavailable; ignoring");
        }

        return new ReadOnlyProbeOutcome(true, "transaction_read_only=on and session default enforced");
    }
}
