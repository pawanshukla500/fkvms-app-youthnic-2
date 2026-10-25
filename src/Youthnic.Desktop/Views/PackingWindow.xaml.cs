using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Views;

public partial class PackingWindow : Window
{
    private const int ReleaseNotesPreviewChars = 600;

    private readonly Marketplace _marketplace;
    private readonly MarketplaceConfig _config;
    private readonly DeskOptions _options;

    private readonly LookupApiClient _apiClient = new();
    private readonly ImageCache _imageCache = new();
    private readonly LookupCoordinator _coordinator;
    private readonly VmsAutomationService _automation;
    private readonly UpdateChecker _updateChecker;

    private readonly ObservableCollection<ProductCardModel> _modalCards = new();
    private DispatcherTimer? _modalTimer;
    private int _modalCountdown;
    private long _currentSeq;

    private ReleaseInfo? _announcedUpdate;
    private string? _downloadedVersion;
    private CancellationTokenSource? _downloadCts;

    private bool _coreReady;
    private bool _automationStarted;
    private bool _processFailedDialogOpen;

    public PackingWindow(Marketplace marketplace, DeskOptions options)
    {
        _marketplace = marketplace;
        _options = options;
        _config = options.For(marketplace);

        InitializeComponent();

        ModalProductItemsControl.ItemsSource = _modalCards;

        Title = $"Youthnic Packing Desk — {_marketplace}  ·  v{UpdateChecker.CurrentVersion()}";
        Panel.SetMarketplace(_marketplace);

        _coordinator = new LookupCoordinator(
            Dispatcher,
            _apiClient,
            _imageCache,
            () => _options.LookupApiUrl,
            () => _options.LookupApiKey,
            () => (_marketplace.ToString().ToLowerInvariant(),
                string.IsNullOrWhiteSpace(_config.SellerAccount) ? null : _config.SellerAccount));

        _coordinator.LookupStarted += (seq, awb) =>
        {
            _currentSeq = seq;
            CloseModal();
            Panel.ShowLoading(seq, awb);
        };
        _coordinator.LookupCompleted += result =>
        {
            Panel.ShowResult(result);
            HandleLookupCompleted(result);
        };
        _coordinator.ImageReady += (seq, image) => Panel.ShowImage(seq, image);
        _coordinator.ItemImageReady += (seq, index, image) =>
        {
            Panel.SetItemImage(seq, index, image);
            if (seq == _currentSeq && index >= 0 && index < _modalCards.Count)
            {
                _modalCards[index].Image = image;
            }
        };
        Panel.RetryRequested += () => _coordinator.RetryLast();

        _automation = new VmsAutomationService(Dispatcher);
        _automation.StateChanged += OnAutomationStateChanged;
        _automation.StatusChanged += message =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                StatusText.Text = message;
                if (message.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("interrupted", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("lost", StringComparison.OrdinalIgnoreCase))
                {
                    RestartButton.Visibility = Visibility.Visible;
                }
            });
        };

        _updateChecker = new UpdateChecker(Dispatcher);
        _updateChecker.UpdateAvailable += OnUpdateAvailable;

        Loaded += async (_, _) =>
        {
            await InitializeWebViewAsync();
            _updateChecker.Start();
        };
    }

    private void HandleLookupCompleted(ScanLookupResult result)
    {
        if (result.Seq != _currentSeq) return; // stale result from older scan

        if (result.Outcome != LookupOutcome.Ok || result.Items.Count == 0)
        {
            CloseModal();
            return;
        }

        var totalUnits = result.Items.Sum(i => i.Quantity);
        var isMulti = result.Items.Count > 1 || totalUnits > 1;

        if (isMulti)
        {
            ShowMultiItemModal(result);
        }
        else
        {
            CloseModal();
        }
    }

    private void ShowMultiItemModal(ScanLookupResult result)
    {
        _modalCards.Clear();
        for (var i = 0; i < result.Items.Count; i++)
        {
            var card = ProductCardModel.FromLookupItem(result.Items[i], i);
            _modalCards.Add(card);
        }

        var totalUnits = result.Items.Sum(i => i.Quantity);
        ModalBannerTitle.Text = $"MULTI-ITEM PARCEL — PACK ALL {totalUnits}";
        ModalBannerSubtitle.Text = $"{totalUnits} items · {result.Items.Count} different SKUs · {result.Awb}";

        _modalCountdown = 5;
        ModalCountdownText.Text = $"Closes in {_modalCountdown}s · click outside or press Esc to close now";

        _modalTimer?.Stop();
        _modalTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _modalTimer.Tick += (_, _) =>
        {
            _modalCountdown--;
            if (_modalCountdown <= 0)
            {
                CloseModal();
            }
            else
            {
                ModalCountdownText.Text = $"Closes in {_modalCountdown}s · click outside or press Esc to close now";
            }
        };
        _modalTimer.Start();

        MultiItemModalOverlay.Visibility = Visibility.Visible;
    }

    private void CloseModal()
    {
        _modalTimer?.Stop();
        _modalTimer = null;
        if (MultiItemModalOverlay.Visibility != Visibility.Collapsed)
        {
            MultiItemModalOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void CloseModal_Click(object sender, RoutedEventArgs e) => CloseModal();

    private void MultiItemModalOverlay_MouseDown(object sender, MouseButtonEventArgs e) => CloseModal();

    private void ModalContent_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent click inside modal dialog from closing it
        e.Handled = true;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && MultiItemModalOverlay.Visibility == Visibility.Visible)
        {
            CloseModal();
            e.Handled = true;
        }
    }

    // ---- In-App Update Flow ------------------------------------------------

    private void OnUpdateAvailable(ReleaseInfo release)
    {
        _announcedUpdate = release;
        PackingUpdateButton.Content = $"⚡ Update Available — v{release.Version}";
        PackingUpdateButton.Visibility = Visibility.Visible;
    }

    private void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        CheckUpdatesButton.Content = "Checking…";
        _ = _updateChecker.CheckNowAsync(CancellationToken.None).ContinueWith(_ =>
            Dispatcher.BeginInvoke(() =>
            {
                CheckUpdatesButton.IsEnabled = true;
                CheckUpdatesButton.Content = "Check for Updates";
                if (_announcedUpdate != null)
                {
                    PackingUpdateButton.Visibility = Visibility.Visible;
                    StatusText.Text = $"Update v{_announcedUpdate.Version} is available!";
                }
                else
                {
                    StatusText.Text = $"App is up to date (v{UpdateChecker.CurrentVersion()})";
                }
            }),
            TaskScheduler.Default);
    }

    private async void PackingUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_announcedUpdate is null) return;

        var notes = _announcedUpdate.Notes.Trim();
        if (notes.Length > ReleaseNotesPreviewChars)
            notes = notes[..ReleaseNotesPreviewChars] + "…";

        var message = $"Version {_announcedUpdate.Version} is available." +
                      Environment.NewLine + Environment.NewLine +
                      (notes.Length > 0 ? notes + Environment.NewLine + Environment.NewLine : string.Empty) +
                      "Download and install now? The application will close and install the new version over the current installation.";

        var confirm = MessageBox.Show(
            this, message, $"Update Desk App — v{_announcedUpdate.Version}",
            MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (confirm != MessageBoxResult.Yes) return;

        await DownloadUpdateAsync(_announcedUpdate);
    }

    private async Task DownloadUpdateAsync(ReleaseInfo release)
    {
        var destination = Path.Combine(UpdateChecker.UpdatesDir, UpdateChecker.SetupAssetName);
        _downloadCts?.Cancel();
        _downloadCts = new CancellationTokenSource();
        var token = _downloadCts.Token;

        PackingUpdateButton.IsEnabled = false;
        PackingUpdateButton.Content = $"Downloading v{release.Version}…";
        UpdateProgressText.Visibility = Visibility.Visible;
        UpdateProgressText.Text = "Preparing download…";

        var progress = new Progress<double>(fraction =>
            Dispatcher.BeginInvoke(() =>
                UpdateProgressText.Text = $"Downloading update… {fraction * 100:0}%"));

        try
        {
            if (_downloadedVersion != release.Version || !File.Exists(destination))
            {
                await _updateChecker.DownloadAsync(release, destination, progress, token);
                _downloadedVersion = release.Version;
            }

            UpdateProgressText.Visibility = Visibility.Collapsed;

            var install = MessageBox.Show(
                this,
                $"The installer for v{release.Version} is downloaded and ready." + Environment.NewLine + Environment.NewLine +
                "Install now? The app will close and install over the current version (no uninstall needed)." +
                Environment.NewLine + "(Click No to install later).",
                "Update Ready",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (install == MessageBoxResult.Yes)
            {
                UpdateChecker.LaunchInstallerAndExit(destination);
            }
            else
            {
                PackingUpdateButton.IsEnabled = true;
                PackingUpdateButton.Content = $"Install v{release.Version} — ready";
            }
        }
        catch (OperationCanceledException)
        {
            UpdateProgressText.Visibility = Visibility.Collapsed;
            PackingUpdateButton.IsEnabled = true;
            PackingUpdateButton.Content = $"⚡ Update Available — v{release.Version}";
        }
        catch (Exception ex)
        {
            AppLog.Error("PackingWindow: Update download failed", ex);
            UpdateProgressText.Visibility = Visibility.Collapsed;
            PackingUpdateButton.IsEnabled = true;
            PackingUpdateButton.Content = $"⚡ Update Available — v{release.Version}";
            MessageBox.Show(
                this, $"The update could not be downloaded:{Environment.NewLine}{ex.Message}",
                "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ---- WebView2 Lifecycle & VMS Automation -------------------------------

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
            await WebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ScanObserver.Script);
            AppLog.Info($"PackingWindow: navigating to {_marketplace} VMS");
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
        _coordinator.CancelPending();
        _automation.Stop();
        _automationStarted = false;
        _currentSeq = 0;
        CloseModal();
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

        _downloadCts?.Cancel();
        _modalTimer?.Stop();
        _updateChecker.Stop();
        _updateChecker.Dispose();
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
