using System.Security.Cryptography;
using System.Text;

namespace Youthnic.LookupApi;

/// <summary>Result of the API-key authorization decision for a request.</summary>
public enum ApiKeyVerdict
{
    /// <summary>Request may proceed.</summary>
    Allow,

    /// <summary>Request must be rejected with HTTP 401.</summary>
    Challenge,
}

/// <summary>
/// Pure decision logic for the LOOKUP_API_KEY gate. Kept free of ASP.NET types so it is
/// trivially unit-testable. Rules:
///   1. No key configured  -> everything open (local dev default).
///   2. Loopback caller    -> always allowed (keeps desk-side local API usable).
///   3. Otherwise          -> caller must present the exact key (constant-time compare).
/// The key itself is never logged or returned; callers may safely log only the verdict.
/// </summary>
public static class ApiKeyGate
{
    public const string HeaderName = "X-Api-Key";

    public static ApiKeyVerdict Evaluate(string? configuredKey, string? presentedKey, bool isLoopback)
    {
        if (string.IsNullOrWhiteSpace(configuredKey))
            return ApiKeyVerdict.Allow;
        if (isLoopback)
            return ApiKeyVerdict.Allow;
        if (string.IsNullOrEmpty(presentedKey))
            return ApiKeyVerdict.Challenge;
        return ConstantTimeEquals(configuredKey, presentedKey)
            ? ApiKeyVerdict.Allow
            : ApiKeyVerdict.Challenge;
    }

    /// <summary>
    /// Constant-time comparison that also hides the key length: both sides are hashed
    /// (SHA-256) before FixedTimeEquals, so neither early-exit nor length leaks anything
    /// about the configured key beyond "a comparison happened".
    /// </summary>
    internal static bool ConstantTimeEquals(string expected, string actual)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }

    /// <summary>Safe-to-log description of a key for diagnostics: length only, never content.</summary>
    public static string DescribeKey(string? key)
        => string.IsNullOrEmpty(key) ? "not configured" : $"configured ({key.Length} chars)";
}
