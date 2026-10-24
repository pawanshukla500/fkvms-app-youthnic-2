using Xunit;
using Youthnic.LookupApi;
using Youthnic.LookupApi.Options;

namespace Youthnic.LookupApi.Tests;

public class ApiKeyGateTests
{
    private const string Key = "super-secret-key-123";

    // --- Decision matrix ---

    [Fact]
    public void KeyNotConfigured_EverythingAllowed()
    {
        Assert.Equal(ApiKeyVerdict.Allow, ApiKeyGate.Evaluate(null, null, isLoopback: false));
        Assert.Equal(ApiKeyVerdict.Allow, ApiKeyGate.Evaluate("", null, isLoopback: false));
        Assert.Equal(ApiKeyVerdict.Allow, ApiKeyGate.Evaluate("  ", "wrong", isLoopback: false));
    }

    [Fact]
    public void KeyConfigured_ValidKey_Allowed()
    {
        Assert.Equal(ApiKeyVerdict.Allow, ApiKeyGate.Evaluate(Key, Key, isLoopback: false));
    }

    [Fact]
    public void KeyConfigured_WrongKey_Challenged()
    {
        Assert.Equal(ApiKeyVerdict.Challenge, ApiKeyGate.Evaluate(Key, "wrong-key", isLoopback: false));
    }

    [Fact]
    public void KeyConfigured_NoKey_Challenged()
    {
        Assert.Equal(ApiKeyVerdict.Challenge, ApiKeyGate.Evaluate(Key, null, isLoopback: false));
        Assert.Equal(ApiKeyVerdict.Challenge, ApiKeyGate.Evaluate(Key, "", isLoopback: false));
    }

    [Fact]
    public void KeyConfigured_LoopbackExempt_AllowedWithoutKey()
    {
        Assert.Equal(ApiKeyVerdict.Allow, ApiKeyGate.Evaluate(Key, null, isLoopback: true));
        Assert.Equal(ApiKeyVerdict.Allow, ApiKeyGate.Evaluate(Key, "wrong-key", isLoopback: true));
    }

    [Fact]
    public void KeyConfigured_LoopbackStillNeedsNoKey_ButValidKeyAlsoAllowed()
    {
        Assert.Equal(ApiKeyVerdict.Allow, ApiKeyGate.Evaluate(Key, Key, isLoopback: true));
    }

    [Fact]
    public void ConstantTimeCompare_ExactMatchRequired()
    {
        Assert.True(ApiKeyGate.ConstantTimeEquals(Key, Key));
        Assert.False(ApiKeyGate.ConstantTimeEquals(Key, Key + "x"));
        Assert.False(ApiKeyGate.ConstantTimeEquals(Key, Key[..^1]));
        Assert.False(ApiKeyGate.ConstantTimeEquals(Key, ""));
    }

    // --- Environment wiring ---

    [Fact]
    public void FromEnvironment_ReadsLookupApiKey()
    {
        var options = LookupApiOptions.FromEnvironment(_ => _ switch
        {
            "LOOKUP_API_KEY" => "  env-key  ",
            _ => null,
        });
        Assert.Equal("env-key", options.ApiKey);
    }

    [Fact]
    public void FromEnvironment_LookupApiKeyDefaultsToEmpty()
    {
        var options = LookupApiOptions.FromEnvironment(_ => null);
        Assert.Equal(string.Empty, options.ApiKey);
    }

    // --- Safe-to-log masking ---

    [Fact]
    public void DescribeKey_NeverLeaksContent()
    {
        Assert.Equal("not configured", ApiKeyGate.DescribeKey(null));
        Assert.Equal("not configured", ApiKeyGate.DescribeKey(""));
        var described = ApiKeyGate.DescribeKey(Key);
        Assert.DoesNotContain(Key, described);
        Assert.Contains("configured", described);
    }
}
