using Xunit;
using Youthnic.LookupApi.Services;

namespace Youthnic.LookupApi.Tests;

public class ReadOnlyProbeTests
{
    [Fact]
    public void RequiredReadOnly_ProbeVerified_Starts()
        => Assert.True(ReadOnlyProbeDecision.ShouldStart(readOnlyRequired: true, new ReadOnlyProbeOutcome(true, "ok")));

    [Fact]
    public void RequiredReadOnly_ProbeNotVerified_RefusesToStart()
        => Assert.False(ReadOnlyProbeDecision.ShouldStart(readOnlyRequired: true, new ReadOnlyProbeOutcome(false, "transaction_read_only=off")));

    [Fact]
    public void RequiredReadOnly_ProbeOutcomeMissing_RefusesToStart()
    {
        // Unreachable database → no outcome → cannot guarantee read-only → refuse.
        Assert.False(ReadOnlyProbeDecision.ShouldStart(readOnlyRequired: true, null));
    }

    [Fact]
    public void ReadOnlyNotRequired_StartsWithoutAnyProbe()
        => Assert.True(ReadOnlyProbeDecision.ShouldStart(readOnlyRequired: false, null));
}
