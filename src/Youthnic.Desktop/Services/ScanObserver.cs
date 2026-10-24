using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Wpf;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Owns the injected scan-observer script (embedded resource). The script attaches
/// a capture-phase keydown listener to the AWB input only — it never cancels or
/// mutates events, so the VMS receives every keystroke exactly as before. This
/// class just injects it once and parses the {type:'scan', awb} messages.
/// </summary>
public static class ScanObserver
{
    private const string ResourceName = "Youthnic.Desktop.Scripts.scan-observer.js";
    private static string? _script;

    public static string Script =>
        _script ??= LoadScript();

    private static string LoadScript()
    {
        var assembly = typeof(ScanObserver).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>Injects the observer if not already present in the page. Safe to call repeatedly.</summary>
    public static async Task EnsureInjectedAsync(WebView2 webView)
    {
        var core = webView.CoreWebView2;
        if (core is null) return;
        try
        {
            await core.ExecuteScriptAsync(Script).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLog.Error("ScanObserver: injection failed", ex);
        }
    }

    public static bool TryParseScanMessage(string json, out string awb)
    {
        awb = string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("type", out var type) ||
                !string.Equals(type.GetString(), "scan", StringComparison.Ordinal))
            {
                return false;
            }
            if (!doc.RootElement.TryGetProperty("awb", out var awbElement)) return false;
            var value = awbElement.GetString();
            if (string.IsNullOrWhiteSpace(value)) return false;
            awb = value.Trim();
            return awb.Length >= 4;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
