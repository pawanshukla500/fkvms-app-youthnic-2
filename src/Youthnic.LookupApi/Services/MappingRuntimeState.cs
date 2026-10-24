using Youthnic.LookupApi.Options;

namespace Youthnic.LookupApi.Services;

/// <summary>
/// Startup-resolved mapping state shared by endpoints. Never fabricate a mapping:
/// either it was configured + validated or auto-matched from discovered metadata, or the
/// API runs in "mapping_pending" mode and /api/lookup returns 503.
/// </summary>
public sealed class MappingRuntimeState
{
    public ForwardScanMapping? Mapping { get; private set; }
    public bool IsReady { get; private set; }
    public string PendingDetail { get; private set; } = "mapping not initialized";
    public string Source { get; private set; } = string.Empty;

    public void SetReady(ForwardScanMapping mapping, string source)
    {
        Mapping = mapping;
        IsReady = true;
        Source = source;
        PendingDetail = string.Empty;
    }

    public void SetPending(string detail)
    {
        Mapping = null;
        IsReady = false;
        Source = string.Empty;
        PendingDetail = detail;
    }
}
