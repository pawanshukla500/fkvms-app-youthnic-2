using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Views;

public partial class PackingWindow : Window
{
    private readonly Marketplace _marketplace;
    private readonly MarketplaceConfig _config;
    private readonly DeskOptions _options;

    private readonly LookupApiClient _apiClient = new();
    private readonly ImageCache _imageCache = new();
    private readonly LookupCoordinator _coordinator;
    private readonly VmsAutomationService _automation;

    private bool _coreReady;
    private bool _automationStarted;
    private bool _processFailedDialogOpen;

    public PackingWindow(Marketplace marketplace, DeskOptions options)
    {
        _marketplace = marketplace;
        _options = options;
        _config = options.For(marketplace);

        InitializeComponent();

        Title = $"Youthnic Packing Desk — {_marketplace}";
        _coordinator = new LookupCoordinator(
            Dispatcher,
            _apiClient,
            _imageCache,
            () => _options.LookupApiUrl,
            () => _options.LookupApiKey,
            () => (_marketplace.ToString().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(_config.SellerAccount) ? null : _config.SellerAccount));
        _coordinator.LookupStarted += (seq, awb) => Panel.ShowLoading(seq, awb);
        _coordinator.LookupCompleted += Panel.ShowResult;
        _coordinator.ImageReady += (seq, image) => Panel.ShowImage(seq, image);
        Panel.RetryRequested += () => _coordinator.RetryLast();

        _automation = new VmsAutomationService(Dispatcher);
        _automation.StateChanged += OnAutomationStateChanged;
        _automation.StatusChanged += message =>
        {
            StatusText.Text = message;
            RestartButton.Visibility = Visibility.Visible;
        };

        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        var profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Youthnic", "PackingDesk", "profiles", _marketplace.ToString().ToLowerInvariant());
        Directory.CreateDirectory(profileDir);

        var envOptions = new CoreWebView2EnvironmentOptions
        {
            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
        };
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: profileDir,
            options: envOptions);

        WebView.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
            {
                ShowFatal($"WebView2 failed to initialize: {args.InitializationException.Message}");
                return;
            }
            ConfigureCoreSettings();
            WireCoreEvents();
            _coreReady = true;
        };

        try
        {
            await WebView.EnsureCoreWebView2Async(environment);
            AppLog.Info($"PackingWindow: navigating to {_marketplace} VMS");
            // Exactly one VMS is ever loaded: the selected marketplace's URL.
            WebView.Source = new Uri(_config.VmsUrl);
        }
        catch (Exception ex)
        {
            AppLog.Error("PackingWindow: WebView2 initialization failed", ex);
            ShowFatal($"WebView2 failed to initialize: {ex.Message}");
        }
    }

    private void ConfigureCoreSettings()
    {
        var core = WebView.CoreWebView2;
        core.Settings.AreDefaultScriptDialogsEnabled = true;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsWebMessageEnabled = true;
    }

    private void WireCoreEvents()
    {
        var core = WebView.CoreWebView2;

        core.PermissionRequested += (_, args) =>
        {
            if (IsVmsOrigin(args.Uri) &&
                (args.PermissionKind is CoreWebView2PermissionKind.Camera
                    or CoreWebView2PermissionKind.Microphone))
            {
                args.State = CoreWebView2PermissionState.Allow;
            }
            else
            {
                args.State = CoreWebView2PermissionState.Deny;
            }
        };

        core.WebMessageReceived += (_, args) =>
        {
            if (ScanObserver.TryParseScanMessage(args.WebMessageAsJson, out var awb))
            {
                _coordinator.HandleScan(awb);
            }
        };

        core.NavigationCompleted += (_, args) =>
        {
            if (!_automationStarted && args.IsSuccess)
            {
                _automationStarted = true;
                _automation.Start(WebView, _marketplace, _config);
            }
            if (!args.IsSuccess)
            {
                StatusText.Text = $"Navigation failed ({args.WebErrorStatus}). Check the VMS URL or network.";
                RestartButton.Visibility = Visibility.Visible;
            }
        };

        core.ProcessFailed += (_, args) =>
        {
            AppLog.Error($"PackingWindow: WebView2 process failed: {args.ProcessFailedKind}/{args.Reason}");
            OnRendererCrashed();
        };
    }

    private bool IsVmsOrigin(string requestUri)
    {
        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri) ||
            !Uri.TryCreate(_config.VmsUrl, UriKind.Absolute, out var vms))
        {
            return false;
        }
        return string.Equals(uri.Host, vms.Host, StringComparison.OrdinalIgnoreCase);
    }

    private void OnAutomationStateChanged(AutomationState state)
    {
        StateText.Text = state switch
        {
            AutomationState.Starting => "Starting VMS…",
            AutomationState.LoggingIn => "Logging in to VMS…",
            AutomationState.ConnectingFeed => "Connecting feed…",
            AutomationState.Packing => "Packing — scan AWB in the VMS",
            AutomationState.Disconnected => "VMS session interrupted",
            _ => state.ToString()
        };

        if (state == AutomationState.Packing)
        {
            StatusText.Text = string.Empty;
            RestartButton.Visibility = Visibility.Collapsed;
        }
    }

    private void OnRendererCrashed()
    {
        if (_processFailedDialogOpen) return;
        _processFailedDialogOpen = true;
        try
        {
            var result = MessageBox.Show(
                this,
                "The VMS browser process crashed. The packing session is interrupted.\n\n" +
                "Restart the session? (The page will reload — do not do this while recording.)",
                "Browser process failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                RestartSession();
            }
            else
            {
                StatusText.Text = "Browser process crashed. Use Restart Session when ready.";
                RestartButton.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _processFailedDialogOpen = false;
        }
    }

    private void RestartSession()
    {
        AppLog.Info("PackingWindow: manual session restart");
        StatusText.Text = string.Empty;
        RestartButton.Visibility = Visibility.Collapsed;
        _automation.Stop();
        _automationStarted = false;
        Panel.ShowIdle();
        if (_coreReady && Uri.TryCreate(_config.VmsUrl, UriKind.Absolute, out var uri))
        {
            WebView.Source = uri;
        }
    }

    private void RestartButton_Click(object sender, RoutedEventArgs e) => RestartSession();

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_automation.IsPackingActive)
        {
            var result = MessageBox.Show(
                this,
                "An active packing session is running. Close anyway?",
                "Confirm close",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }

        _coordinator.CancelPending();
        _automation.Stop();
        _automation.Dispose();
        _apiClient.Dispose();
    }

    private void ShowFatal(string message)
    {
        StateText.Text = message;
        MessageBox.Show(this, message, "Packing Desk", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
