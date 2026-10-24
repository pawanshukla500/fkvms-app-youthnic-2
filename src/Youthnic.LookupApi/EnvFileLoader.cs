namespace Youthnic.LookupApi;

/// <summary>
/// Loads a repo-root .env file at startup for dev convenience. Values already present
/// in the process environment always win — this file overrides nothing. Values are never
/// logged, only key names (at Debug level).
/// </summary>
public static class EnvFileLoader
{
    public static int Load()
    {
        var path = FindEnvFile();
        if (path is null) return 0;

        var count = 0;
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var eq = line.IndexOf('=');
            if (eq <= 0) continue;

            var key = line[..eq].Trim();
            if (key.Length == 0 || key.Any(c => !(char.IsLetterOrDigit(c) || c == '_'))) continue;

            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];

            // Never override an existing environment variable.
            if (Environment.GetEnvironmentVariable(key) is null)
            {
                Environment.SetEnvironmentVariable(key, value);
                count++;
            }
        }
        return count;
    }

    private static string? FindEnvFile()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, ".env");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }
}
