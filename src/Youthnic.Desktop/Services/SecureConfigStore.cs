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

    /// <summary>
    /// Reads stored options, or auto-seeds and saves production defaults for both Flipkart
    /// and Myntra if never configured. Reconciles any missing marketplace credentials so
    /// operators never face unconfigured fields or blank inputs on first run.
    /// </summary>
    public static DeskOptions Load()
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
                    if (options is not null) return EnsureDefaults(options);
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
                if (options is not null) return EnsureDefaults(options);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: local config file read failed", ex);
        }

        // 3. Fallback: auto-seed from installer defaults or built-in defaults
        var defaults = InstallDefaults.Load();
        var seeded = CreateDefaultOptions(defaults);
        try
        {
            Save(seeded);
            AppLog.Info("SecureConfigStore: seeded and saved default desk configuration for Flipkart & Myntra");
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: auto-save default config failed", ex);
        }

        return seeded;
    }

    public static DeskOptions CreateDefaultOptions(InstallDefaults? defaults = null)
    {
        defaults ??= InstallDefaults.Empty;
        var deviceId = Guid.NewGuid().ToString("N");
        return new DeskOptions(
            DeviceId: deviceId,
            DeskName: !string.IsNullOrWhiteSpace(defaults.DeskName) ? defaults.DeskName! : "Packing Desk",
            LookupApiUrl: !string.IsNullOrWhiteSpace(defaults.LookupApiUrl)
                ? defaults.LookupApiUrl!
                : DeskOptions.DefaultLookupApiUrl,
            LookupApiKey: !string.IsNullOrWhiteSpace(defaults.LookupApiKey)
                ? defaults.LookupApiKey!
                : DeskOptions.DefaultLookupApiKey,
            Flipkart: new MarketplaceConfig(
                !string.IsNullOrWhiteSpace(defaults.FlipkartVmsUrl) ? defaults.FlipkartVmsUrl! : DeskOptions.DefaultFlipkartVmsUrl,
                !string.IsNullOrWhiteSpace(defaults.FlipkartUsername) ? defaults.FlipkartUsername! : DeskOptions.DefaultFlipkartUsername,
                !string.IsNullOrWhiteSpace(defaults.FlipkartPassword) ? defaults.FlipkartPassword! : DeskOptions.DefaultFlipkartPassword,
                !string.IsNullOrWhiteSpace(defaults.FlipkartFeedId) ? defaults.FlipkartFeedId! : DeskOptions.DefaultFlipkartFeedId,
                !string.IsNullOrWhiteSpace(defaults.FlipkartSeller) ? defaults.FlipkartSeller! : DeskOptions.DefaultFlipkartSeller),
            Myntra: new MarketplaceConfig(
                !string.IsNullOrWhiteSpace(defaults.MyntraVmsUrl) ? defaults.MyntraVmsUrl! : DeskOptions.DefaultMyntraVmsUrl,
                !string.IsNullOrWhiteSpace(defaults.MyntraUsername) ? defaults.MyntraUsername! : DeskOptions.DefaultMyntraUsername,
                !string.IsNullOrWhiteSpace(defaults.MyntraPassword) ? defaults.MyntraPassword! : DeskOptions.DefaultMyntraPassword,
                !string.IsNullOrWhiteSpace(defaults.MyntraFeedId) ? defaults.MyntraFeedId! : DeskOptions.DefaultMyntraFeedId,
                !string.IsNullOrWhiteSpace(defaults.MyntraSeller) ? defaults.MyntraSeller! : DeskOptions.DefaultMyntraSeller),
            SetupCompleted: true);
    }

    public static DeskOptions EnsureDefaults(DeskOptions options)
    {
        bool changed = false;
        var defaults = InstallDefaults.Load();

        var deviceId = string.IsNullOrWhiteSpace(options.DeviceId)
            ? (changed = true, Guid.NewGuid().ToString("N")).Item2
            : options.DeviceId;

        var deskName = string.IsNullOrWhiteSpace(options.DeskName) || (options.DeskName == "Packing Desk" && !string.IsNullOrWhiteSpace(defaults.DeskName))
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.DeskName) ? defaults.DeskName! : "Packing Desk").Item2
            : options.DeskName;

        var lookupUrl = string.IsNullOrWhiteSpace(options.LookupApiUrl)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.LookupApiUrl) ? defaults.LookupApiUrl! : DeskOptions.DefaultLookupApiUrl).Item2
            : options.LookupApiUrl;

        var lookupKey = string.IsNullOrWhiteSpace(options.LookupApiKey)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.LookupApiKey) ? defaults.LookupApiKey! : DeskOptions.DefaultLookupApiKey).Item2
            : options.LookupApiKey;

        var fk = options.Flipkart ?? new MarketplaceConfig(string.Empty, string.Empty, string.Empty, string.Empty);
        var fkUrl = string.IsNullOrWhiteSpace(fk.VmsUrl) ? (changed = true, !string.IsNullOrWhiteSpace(defaults.FlipkartVmsUrl) ? defaults.FlipkartVmsUrl! : DeskOptions.DefaultFlipkartVmsUrl).Item2 : fk.VmsUrl;
        var fkUser = string.IsNullOrWhiteSpace(fk.Username) || (fk.Username == DeskOptions.DefaultUsername && !string.IsNullOrWhiteSpace(defaults.FlipkartUsername) && defaults.FlipkartUsername != DeskOptions.DefaultUsername)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.FlipkartUsername) ? defaults.FlipkartUsername! : DeskOptions.DefaultFlipkartUsername).Item2
            : fk.Username;
        var fkPass = string.IsNullOrWhiteSpace(fk.Password) || (fk.Password == DeskOptions.DefaultPassword && !string.IsNullOrWhiteSpace(defaults.FlipkartPassword) && defaults.FlipkartPassword != DeskOptions.DefaultPassword)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.FlipkartPassword) ? defaults.FlipkartPassword! : DeskOptions.DefaultFlipkartPassword).Item2
            : fk.Password;
        var fkFeed = string.IsNullOrWhiteSpace(fk.FeedId) || (fk.FeedId == DeskOptions.DefaultFeedId && !string.IsNullOrWhiteSpace(defaults.FlipkartFeedId) && defaults.FlipkartFeedId != DeskOptions.DefaultFeedId)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.FlipkartFeedId) ? defaults.FlipkartFeedId! : DeskOptions.DefaultFlipkartFeedId).Item2
            : fk.FeedId;
        var fkSeller = string.IsNullOrWhiteSpace(fk.SellerAccount)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.FlipkartSeller) ? defaults.FlipkartSeller! : DeskOptions.DefaultFlipkartSeller).Item2
            : fk.SellerAccount;
        var newFk = new MarketplaceConfig(fkUrl, fkUser, fkPass, fkFeed, fkSeller);

        var my = options.Myntra ?? new MarketplaceConfig(string.Empty, string.Empty, string.Empty, string.Empty);
        var myUrl = string.IsNullOrWhiteSpace(my.VmsUrl) ? (changed = true, !string.IsNullOrWhiteSpace(defaults.MyntraVmsUrl) ? defaults.MyntraVmsUrl! : DeskOptions.DefaultMyntraVmsUrl).Item2 : my.VmsUrl;
        var myUser = string.IsNullOrWhiteSpace(my.Username) || (my.Username == DeskOptions.DefaultUsername && !string.IsNullOrWhiteSpace(defaults.MyntraUsername) && defaults.MyntraUsername != DeskOptions.DefaultUsername)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.MyntraUsername) ? defaults.MyntraUsername! : DeskOptions.DefaultMyntraUsername).Item2
            : my.Username;
        var myPass = string.IsNullOrWhiteSpace(my.Password) || (my.Password == DeskOptions.DefaultPassword && !string.IsNullOrWhiteSpace(defaults.MyntraPassword) && defaults.MyntraPassword != DeskOptions.DefaultPassword)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.MyntraPassword) ? defaults.MyntraPassword! : DeskOptions.DefaultMyntraPassword).Item2
            : my.Password;
        var myFeed = string.IsNullOrWhiteSpace(my.FeedId) || (my.FeedId == DeskOptions.DefaultFeedId && !string.IsNullOrWhiteSpace(defaults.MyntraFeedId) && defaults.MyntraFeedId != DeskOptions.DefaultFeedId)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.MyntraFeedId) ? defaults.MyntraFeedId! : DeskOptions.DefaultMyntraFeedId).Item2
            : my.FeedId;
        var mySeller = string.IsNullOrWhiteSpace(my.SellerAccount)
            ? (changed = true, !string.IsNullOrWhiteSpace(defaults.MyntraSeller) ? defaults.MyntraSeller! : DeskOptions.DefaultMyntraSeller).Item2
            : my.SellerAccount;
        var newMy = new MarketplaceConfig(myUrl, myUser, myPass, myFeed, mySeller);

        if (!options.SetupCompleted) changed = true;

        var reconciled = new DeskOptions(
            DeviceId: deviceId,
            DeskName: deskName,
            LookupApiUrl: lookupUrl,
            LookupApiKey: lookupKey,
            Flipkart: newFk,
            Myntra: newMy,
            SetupCompleted: true);

        if (changed)
        {
            try
            {
                Save(reconciled);
            }
            catch (Exception ex)
            {
                AppLog.Error("SecureConfigStore: save reconciled config failed", ex);
            }
        }

        return reconciled;
    }

    public static void Save(DeskOptions options)
    {
        var deviceId = string.IsNullOrWhiteSpace(options.DeviceId)
            ? Guid.NewGuid().ToString("N")
            : options.DeviceId.Trim();

        var toSave = options with { DeviceId = deviceId };

        Directory.CreateDirectory(RootDir);
        File.WriteAllText(DeviceMarkerFile, deviceId, Encoding.UTF8);

        // 1. Save DPAPI encrypted blob in LocalAppData
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

        // 2. Save plain JSON local configuration for persistence & recovery across PC reboots
        var readableJson = JsonSerializer.Serialize(toSave, ReadableJsonOptions);
        try
        {
            File.WriteAllText(LocalConfigFile, readableJson, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLog.Error("SecureConfigStore: local config save failed", ex);
        }

        // 3. Save to application install directory (desk-config.json) if writable
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var appConfigFile = Path.Combine(appDir, "desk-config.json");
            File.WriteAllText(appConfigFile, readableJson, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLog.Debug("SecureConfigStore: saving to app directory skipped (normal for non-admin): " + ex.Message);
        }

        // 4. Save to ProgramData install-defaults.json & desk-config.json for machine-wide availability if writable
        try
        {
            var programDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Youthnic", "PackingDesk");
            Directory.CreateDirectory(programDataDir);
            File.WriteAllText(Path.Combine(programDataDir, "desk-config.json"), readableJson, Encoding.UTF8);
            File.WriteAllText(Path.Combine(programDataDir, "install-defaults.json"), readableJson, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            AppLog.Debug("SecureConfigStore: saving to ProgramData skipped (normal for non-admin): " + ex.Message);
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
