using System.IO;
using System.Text.Json;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Optional first-run defaults written by the installer (admin-entered) to
/// %ProgramData%\Youthnic\PackingDesk\install-defaults.json. Lets IT pre-seed the
/// lookup API URL + key (for the hosted lookup server) and suggested seller-account
/// names so the on-desk setup wizard is a quick review instead of re-typing.
/// The API key is a desk-to-server shared secret, not a marketplace credential;
/// operators still confirm everything and marketplace credentials are entered per desk.
/// </summary>
public sealed record InstallDefaults(
    string? LookupApiUrl,
    string? FlipkartSeller,
    string? MyntraSeller,
    string? LookupApiKey)
{
    public static InstallDefaults Empty { get; } = new(null, null, null, null);

    public static InstallDefaults Load()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Youthnic", "PackingDesk", "install-defaults.json");
            if (!File.Exists(path)) return Empty;

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<InstallDefaults>(stream,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? Empty;
        }
        catch (Exception ex)
        {
            AppLog.Debug("InstallDefaults: could not read install defaults — " + ex.Message);
            return Empty;
        }
    }
}
