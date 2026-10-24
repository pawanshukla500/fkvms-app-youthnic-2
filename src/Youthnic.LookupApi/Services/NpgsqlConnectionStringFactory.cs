using Npgsql;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Builds the Npgsql connection string. The read-only session default is ALWAYS appended
/// (Options=-c default_transaction_read_only=on) regardless of any Options the URL carries,
/// so even ad-hoc sessions from this app cannot write. Never log the result — it contains credentials.
/// </summary>
public static class NpgsqlConnectionStringFactory
{
    public const string ReadOnlySessionOption = "-c default_transaction_read_only=on";

    public static string Build(string databaseUrl, int queryTimeoutMs)
    {
        if (string.IsNullOrWhiteSpace(databaseUrl))
            throw new InvalidOperationException("VMS_DATABASE_URL is required but was not set.");

        var builder = new NpgsqlConnectionStringBuilder();
        if (databaseUrl.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            databaseUrl.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            ApplyUri(builder, databaseUrl);
        }
        else
        {
            builder.ConnectionString = databaseUrl;
        }

        builder.Options = string.IsNullOrWhiteSpace(builder.Options)
            ? ReadOnlySessionOption
            : builder.Options + " " + ReadOnlySessionOption;
        builder.CommandTimeout = Math.Clamp((queryTimeoutMs + 999) / 1000, 1, 30);
        builder.MinPoolSize = 2;
        builder.MaxPoolSize = Math.Min(builder.MaxPoolSize, 10);
        builder.ConnectionIdleLifetime = 30;
        builder.ApplicationName = "youthnic-lookup-api";
        return builder.ConnectionString;
    }

    private static void ApplyUri(NpgsqlConnectionStringBuilder builder, string databaseUrl)
    {
        Uri uri;
        try
        {
            uri = new Uri(databaseUrl);
        }
        catch (UriFormatException ex)
        {
            throw new InvalidOperationException("VMS_DATABASE_URL is not a valid postgres:// URI.", ex);
        }

        builder.Host = uri.Host;
        builder.Port = uri.Port > 0 ? uri.Port : 5432;
        builder.Database = uri.AbsolutePath.Trim('/');
        if (builder.Database.Length == 0)
            throw new InvalidOperationException("VMS_DATABASE_URL does not contain a database name.");

        var userInfo = uri.UserInfo.Split(':', 2);
        if (userInfo.Length > 0 && userInfo[0].Length > 0)
            builder.Username = Uri.UnescapeDataString(userInfo[0]);
        if (userInfo.Length > 1 && userInfo[1].Length > 0)
            builder.Password = Uri.UnescapeDataString(userInfo[1]);

        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(kv[0]).ToLowerInvariant();
            var value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";
            switch (key)
            {
                case "sslmode": if (Enum.TryParse<SslMode>(value, ignoreCase: true, out var ssl)) builder.SslMode = ssl; break;
                case "connect_timeout": if (int.TryParse(value, out var ct)) builder.Timeout = ct; break;
                case "application_name": builder.ApplicationName = value; break;
            }
        }
    }
}
