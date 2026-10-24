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
    private static readonly JsonSerializerOptions ReadableJsonOptions = new() { WriteIndented = true };

    private static string RootDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Youthnic", "PackingDesk");

    private static string DeviceMarkerFile => Path.Combine(RootDir, "device.id");

    private static string ConfigFilePath(string deviceId) =>
        Path.Combine(RootDir, $"config-{deviceId}.dat");

    private static string LocalConfigFile => Path.Combine(RootDir, "desk-config.json");

    /// <summary>Reads the stored options, or null if never configured / unreadable / corrupt.</summary>
    public static DeskOptions? Load()
    {
        // 1. Try loading from DPAPI-encrypted file
        try
        {
            var deviceId = ReadDeviceMarker();
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var path = ConfigFilePath(deviceId);
                if (File.Exists(path))
                {
                    var encrypted = File.ReadAllBytes(path);
                    var plain = ProtectedData.Unprotect(encrypted, optionalEntropy: null, DataProtectionScope.CurrentUser);
                    var options = JsonSerializer.Deserialize<DeskOptions>(plain, JsonOptions);
                    if (options is { SetupCompleted: true }) return options;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Debug("SecureConfigStore: DPAPI load skipped: " + ex.Message);
        }

        // 2. Fallback to readable local config file if DPAPI load was empty
        try
        {
            if (File.Exists(LocalConfigFile))
            {
                var text = File.ReadAllText(LocalConfigFile, Encoding.UTF8);
                var options = JsonSerializer.Deserialize<DeskOptions>(text, ReadableJsonOptions);
                if (options is { SetupCompleted: true }) return options;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: local config file read failed", ex);
        }

        return null;
    }

    public static void Save(DeskOptions options)
    {
        var deviceId = string.IsNullOrWhiteSpace(options.DeviceId)
            ? Guid.NewGuid().ToString("N")
            : options.DeviceId.Trim();

        var toSave = options with { DeviceId = deviceId };

        Directory.CreateDirectory(RootDir);
        File.WriteAllText(DeviceMarkerFile, deviceId, Encoding.UTF8);

        // Save DPAPI encrypted blob
        try
        {
            var plain = JsonSerializer.SerializeToUtf8Bytes(toSave, JsonOptions);
            var encrypted = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(ConfigFilePath(deviceId), encrypted);
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: DPAPI save failed", ex);
        }

        // Save plain JSON local configuration for persistence & recovery across PC reboots
        try
        {
            var readableJson = JsonSerializer.Serialize(toSave, ReadableJsonOptions);
            File.WriteAllText(LocalConfigFile, readableJson, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: local config save failed", ex);
        }
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
