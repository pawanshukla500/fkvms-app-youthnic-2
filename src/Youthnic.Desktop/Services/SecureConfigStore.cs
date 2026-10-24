using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Youthnic.Desktop.Models;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Persists DeskOptions as JSON encrypted with DPAPI (CurrentUser scope) so the
/// blob is only readable on this machine by this Windows user. Storage is unique
/// per device: the encrypted blob lives in config-&lt;DeviceId&gt;.dat while the
/// (non-secret) DeviceId itself is kept in a plain bootstrap marker file.
/// A missing or unreadable/corrupt file is treated as "not configured".
/// </summary>
public static class SecureConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private static string RootDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Youthnic", "PackingDesk");

    private static string DeviceMarkerFile => Path.Combine(RootDir, "device.id");

    private static string ConfigFilePath(string deviceId) =>
        Path.Combine(RootDir, $"config-{deviceId}.dat");

    /// <summary>Reads the stored options, or null if never configured / unreadable / corrupt.</summary>
    public static DeskOptions? Load()
    {
        try
        {
            var deviceId = ReadDeviceMarker();
            if (string.IsNullOrWhiteSpace(deviceId)) return null;

            var path = ConfigFilePath(deviceId);
            if (!File.Exists(path)) return null;

            var encrypted = File.ReadAllBytes(path);
            var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var options = JsonSerializer.Deserialize<DeskOptions>(plain, JsonOptions);
            return options is null or { SetupCompleted: false } ? null : options;
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: load failed; treating as not configured", ex);
            return null;
        }
    }

    public static void Save(DeskOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DeviceId))
            throw new ArgumentException("DeviceId must not be empty.", nameof(options));

        Directory.CreateDirectory(RootDir);
        File.WriteAllText(DeviceMarkerFile, options.DeviceId, Encoding.UTF8);

        var plain = JsonSerializer.SerializeToUtf8Bytes(options, JsonOptions);
        var encrypted = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(ConfigFilePath(options.DeviceId), encrypted);
    }

    private static string? ReadDeviceMarker()
    {
        try
        {
            return File.Exists(DeviceMarkerFile)
                ? File.ReadAllText(DeviceMarkerFile, Encoding.UTF8).Trim()
                : null;
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: device marker unreadable", ex);
            return null;
        }
    }
}
