using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Wpf;
using Youthnic.Desktop.Models;

namespace Youthnic.Desktop.Services;

public enum AutomationState
{
    Starting,
    LoggingIn,
    ConnectingFeed,
    Packing,
    Disconnected
}

public sealed record VmsProbe(
    string Url,
    bool Username,
    bool Password,
    bool Feed,
    bool Awb,
    bool FeedFilled,
    bool LoginVisible,
    bool ConnectVisible,
    bool RecordVisible,
    bool LoginLoading = false)
{
    public bool OnLogin => Url.Contains("#/login", StringComparison.OrdinalIgnoreCase)
        || Url.EndsWith("/login", StringComparison.OrdinalIgnoreCase)
        || (LoginVisible && Username && Password);

    public bool OnOperatorVms => Url.Contains("#/operator/vms", StringComparison.OrdinalIgnoreCase)
        || Url.Contains("/operator/vms", StringComparison.OrdinalIgnoreCase)
        || Feed
        || Awb
        || RecordVisible
        || ConnectVisible;
}

/// <summary>
/// Drives the VMS SPA through login → feed connect → packing by probing the live DOM
/// (≥500 ms throttle) and filling/clicking only what the operator has not already done.
/// Session expiry (router back to #/login) re-runs login automatically, enabling shifts
/// to continue 4–5+ hours uninterrupted. It never touches the AWB value, never reloads
/// mid-recording, never intercepts input, and guards every action by a fresh DOM probe.
/// </summary>
public sealed class VmsAutomationService : IDisposable
{
    public const int PollIntervalMs = 700;
    public const int LoginFillToClickDelayMs = 800;
    public const int FeedFillToClickDelayMs = 600;

    /// <summary>Ticks without any packing DOM before a disconnect is reported (Packing state).</summary>
    private const int MissingDomThreshold = 15;

    private static readonly JsonSerializerOptions ProbeJsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Dispatcher _dispatcher;
    private WebView2? _webView;
    private MarketplaceConfig? _config;
    private Marketplace _marketplace;

    private CancellationTokenSource? _loopCts;
    private volatile bool _tickInProgress;

    private int _missingDomTicks;
    private bool _disconnectReported;
    private bool _connectClicked;
    private int _connectCooldownTicks;
    private bool _loginSubmitted;
    private int _loginSubmittedTicks;

    public AutomationState State { get; private set; } = AutomationState.Starting;

    public bool IsPackingActive => State == AutomationState.Packing;

    /// <summary>Raised on the UI dispatcher whenever the automation state changes.</summary>
    public event Action<AutomationState>? StateChanged;

    /// <summary>Raised on the UI dispatcher for human-readable status/recovery messages.</summary>
    public event Action<string>? StatusChanged;

    public VmsAutomationService(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Start(WebView2 webView, Marketplace marketplace, MarketplaceConfig config)
    {
        Stop();
        _webView = webView;
        _marketplace = marketplace;
        _config = config;
        _missingDomTicks = 0;
        _disconnectReported = false;
        _connectClicked = false;
        _connectCooldownTicks = 0;
        _loginSubmitted = false;
        _loginSubmittedTicks = 0;
        State = AutomationState.Starting;
        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(AutomationState.Starting));
        _loopCts = new CancellationTokenSource();
        _ = Task.Run(() => LoopAsync(_loopCts.Token));
    }

    public void UpdateConfig(MarketplaceConfig config)
    {
        _config = config;
        AppLog.Info($"VmsAutomation: updated credentials/feed for {_marketplace} (User: {config.Username}, Feed: {config.FeedId})");
    }

    public void Stop()
    {
        _loopCts?.Cancel();
        _loopCts?.Dispose();
        _loopCts = null;
        _loginSubmitted = false;
        _loginSubmittedTicks = 0;
        _connectClicked = false;
    }

    public void Dispose() => Stop();

    private async Task LoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(PollIntervalMs, ct).ConfigureAwait(false);
                if (_tickInProgress) continue;
                _tickInProgress = true;
                try
                {
                    await TickAsync(ct).ConfigureAwait(false);
                }
                finally
                {
                    _tickInProgress = false;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                AppLog.Error("VmsAutomation: tick failed", ex);
            }
        }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var raw = await EvalOnUiAsync(VmsScripts.Probe).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return;

        VmsProbe? probe;
        try
        {
            var unquoted = JsonSerializer.Deserialize<string>(raw);
            probe = string.IsNullOrWhiteSpace(unquoted)
                ? null
                : JsonSerializer.Deserialize<VmsProbe>(unquoted, ProbeJsonOptions);
        }
        catch (JsonException)
        {
            return;
        }
        if (probe is null || string.IsNullOrWhiteSpace(probe.Url)) return;

        ct.ThrowIfCancellationRequested();

        switch (State)
        {
            case AutomationState.Starting:
                await HandleStartingAsync(probe).ConfigureAwait(false);
                break;

            case AutomationState.LoggingIn:
                await HandleLoggingInAsync(probe, ct).ConfigureAwait(false);
                break;

            case AutomationState.ConnectingFeed:
                await HandleConnectingFeedAsync(probe, ct).ConfigureAwait(false);
                break;

            case AutomationState.Packing:
                await HandlePackingAsync(probe).ConfigureAwait(false);
                break;
        }
    }

    private async Task HandleStartingAsync(VmsProbe probe)
    {
        if (probe.OnLogin)
        {
            AppLog.Info($"VmsAutomation: login page detected for {_marketplace}");
            SetState(AutomationState.LoggingIn);
            return;
        }

        if (probe.OnOperatorVms)
        {
            if (probe.Awb || probe.RecordVisible)
            {
                await EnterPackingAsync().ConfigureAwait(false);
            }
            else
            {
                SetState(AutomationState.ConnectingFeed);
            }
        }
    }

    private async Task HandleLoggingInAsync(VmsProbe probe, CancellationToken ct)
    {
        var config = _config;
        if (config is null) return;

        if (!probe.OnLogin)
        {
            _loginSubmitted = false;
            _loginSubmittedTicks = 0;
            if (probe.OnOperatorVms)
            {
                SetState((probe.Awb || probe.RecordVisible) ? AutomationState.Packing : AutomationState.ConnectingFeed);
                if (State == AutomationState.Packing) await EnterPackingAsync().ConfigureAwait(false);
            }
            else
            {
                SetState(AutomationState.Starting);
            }
            return;
        }

        if (_loginSubmitted)
        {
            _loginSubmittedTicks++;
            if (probe.LoginLoading || _loginSubmittedTicks < 12)
            {
                return;
            }
            _loginSubmitted = false;
        }

        if (probe.Username && probe.Password)
        {
            var userStatus = await EvalOnUiAsync(VmsScripts.FillByName("Username", config.Username)).ConfigureAwait(false);
            var uStatus = userStatus?.Trim('"');
            if (string.Equals(uStatus, "typing", StringComparison.OrdinalIgnoreCase))
            {
                RaiseStatus("Entering username…");
                await Task.Delay(LoginFillToClickDelayMs, ct).ConfigureAwait(false);
                return;
            }

            var passStatus = await EvalOnUiAsync(VmsScripts.FillByName("Password", config.Password)).ConfigureAwait(false);
            var pStatus = passStatus?.Trim('"');
            if (string.Equals(pStatus, "typing", StringComparison.OrdinalIgnoreCase))
            {
                RaiseStatus("Entering password…");
                await Task.Delay(LoginFillToClickDelayMs, ct).ConfigureAwait(false);
                return;
            }

            RaiseStatus("Logging in to VMS…");
            await Task.Delay(LoginFillToClickDelayMs, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var click = await EvalOnUiAsync(VmsScripts.ClickByName("Login")).ConfigureAwait(false);
            if (click?.Contains("clicked", StringComparison.Ordinal) == true)
            {
                AppLog.Info("VmsAutomation: login submitted, waiting for operator VMS");
                _loginSubmitted = true;
                _loginSubmittedTicks = 0;
            }
        }
    }

    private async Task HandleConnectingFeedAsync(VmsProbe probe, CancellationToken ct)
    {
        var config = _config;
        if (config is null) return;

        if (probe.OnLogin)
        {
            AppLog.Info("VmsAutomation: session expired, re-running login");
            _connectClicked = false;
            SetState(AutomationState.LoggingIn);
            return;
        }

        if (probe.Awb || probe.RecordVisible)
        {
            await EnterPackingAsync().ConfigureAwait(false);
            return;
        }

        if (probe.Feed)
        {
            var fillStatus = await EvalOnUiAsync(VmsScripts.FillByPlaceholder("Feed", config.FeedId)).ConfigureAwait(false);
            var statusStr = fillStatus?.Trim('"');
            if (string.Equals(statusStr, "typing", StringComparison.OrdinalIgnoreCase))
            {
                RaiseStatus($"Entering feed ID {config.FeedId}…");
                await Task.Delay(FeedFillToClickDelayMs, ct).ConfigureAwait(false);
                return;
            }

            if (!_connectClicked)
            {
                RaiseStatus("Connecting camera feed…");
                await Task.Delay(FeedFillToClickDelayMs, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                var click = await EvalOnUiAsync(VmsScripts.ClickByName("Connect")).ConfigureAwait(false);
                if (click?.Contains("clicked", StringComparison.Ordinal) == true)
                {
                    _connectClicked = true;
                    _connectCooldownTicks = 0;
                    AppLog.Debug($"VmsAutomation: connect clicked for {_marketplace}, awaiting camera stream");
                }
            }
            else
            {
                _connectCooldownTicks++;
                if (_connectCooldownTicks > 10 && !probe.FeedFilled)
                {
                    _connectClicked = false;
                }
            }
            return;
        }

        TrackMissingDom("Feed connect screen not detected yet");
    }

    private async Task HandlePackingAsync(VmsProbe probe)
    {
        if (probe.OnLogin)
        {
            AppLog.Info("VmsAutomation: session expired during packing, auto-relogging for extended shift");
            _missingDomTicks = 0;
            _disconnectReported = false;
            _connectClicked = false;
            _loginSubmitted = false;
            SetState(AutomationState.LoggingIn);
            return;
        }

        if (probe.Feed && !probe.Awb && !probe.RecordVisible)
        {
            AppLog.Info("VmsAutomation: camera feed dropped during extended shift, auto-reconnecting feed");
            _missingDomTicks = 0;
            _disconnectReported = false;
            _connectClicked = false;
            SetState(AutomationState.ConnectingFeed);
            return;
        }

        if (probe.Awb || probe.RecordVisible)
        {
            if (_disconnectReported)
            {
                _disconnectReported = false;
                RaiseStatus("VMS packing screen recovered.");
            }
            _missingDomTicks = 0;
            return;
        }

        TrackMissingDom("VMS packing screen lost (camera/feed disconnected or page changed)");
        await Task.CompletedTask;
    }

    private void TrackMissingDom(string message)
    {
        _missingDomTicks++;
        if (_missingDomTicks < MissingDomThreshold || _disconnectReported) return;
        _disconnectReported = true;
        AppLog.Error("VmsAutomation: " + message);
        RaiseStatus(message + " — use Restart Session to reconnect. Recording is left untouched.");
    }

    private async Task EnterPackingAsync()
    {
        SetState(AutomationState.Packing);
        _missingDomTicks = 0;
        _disconnectReported = false;
        var op = _dispatcher.InvokeAsync(async () =>
        {
            if (_webView is not null)
            {
                await ScanObserver.EnsureInjectedAsync(_webView).ConfigureAwait(false);
            }
        });
        await op.Task.Unwrap().ConfigureAwait(false);
    }

    private void SetState(AutomationState state)
    {
        if (State == state) return;
        State = state;
        _connectClicked = false;
        _dispatcher.BeginInvoke(() => StateChanged?.Invoke(state));
    }

    private void RaiseStatus(string message) =>
        _dispatcher.BeginInvoke(() => StatusChanged?.Invoke(message));

    private Task<string?> EvalOnUiAsync(string script)
    {
        var op = _dispatcher.InvokeAsync(async () =>
        {
            var core = _webView?.CoreWebView2;
            if (core is null) return null;
            return await core.ExecuteScriptAsync(script);
        });
        return op.Task.Unwrap();
    }
}
