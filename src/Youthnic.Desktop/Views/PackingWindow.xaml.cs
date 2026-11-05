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

    private MarketplaceConfig _config;

    private DeskOptions _options;



    private readonly LookupApiClient _apiClient = new();

    private readonly ImageCache _imageCache = new();

    private readonly LookupCoordinator _coordinator;

    private readonly VmsAutomationService _automation;

    private readonly UpdateChecker _updateChecker;



    private MultiItemModalWindow? _modalWindow;

    private DispatcherTimer? _maintenanceTimer;

    private long _currentSeq;



    private int _shiftPackCount;

    private int _shiftTotalUnits;



    private ReleaseInfo? _announcedUpdate;

    private string? _downloadedVersion;

    private CancellationTokenSource? _downloadCts;



    private bool _coreReady;

    private bool _automationStarted;

    private bool _processFailedDialogOpen;

    public bool IsSwitchingDesk { get; private set; }

    private bool _isClosing;



    public PackingWindow(Marketplace marketplace, DeskOptions options)

    {

        _marketplace = marketplace;

        _options = options;

        _config = options.For(marketplace);



        InitializeComponent();



        Title = $"Youthnic Packing Desk — {_marketplace} ({_options.DeskName})  ·  v{UpdateChecker.CurrentVersion()}";

        Panel.SetMarketplace(_marketplace);



        _coordinator = new LookupCoordinator(

            Dispatcher,

            _apiClient,

            _imageCache,

            () => _options.LookupApiUrl,

            () => _options.LookupApiKey,

            () => (_marketplace.ToString().ToLowerInvariant(),

                ResolveDesktopSellerParameter(_marketplace, _config.SellerAccount)));



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

            if (seq == _currentSeq)

            {

                _modalWindow?.SetItemImage(index, image);

            }

        };

        Panel.RetryRequested += () => _coordinator.RetryLast();



        _automation = new VmsAutomationService(Dispatcher);

        _automation.StateChanged += OnAutomationStateChanged;

        _automation.UploadStatusChanged += OnUploadStatusChanged;

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

        _updateChecker.UpdateReadyToInstall += OnUpdateReadyToInstall;



        Activated += async (_, _) => await FocusScanBarAsync();



        Loaded += async (_, _) =>

        {

            if (_isClosing) return;

            await InitializeWebViewAsync();

            if (_isClosing) return;

            _updateChecker.Start();

            _maintenanceTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };

            _maintenanceTimer.Tick += (_, _) => PerformMaintenance();

            _maintenanceTimer.Start();

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

        _shiftPackCount++;

        _shiftTotalUnits += totalUnits;

        ShiftPacksText.Text = $"Shift: {_shiftPackCount} packs ({_shiftTotalUnits} units)";



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

        CloseModal();

        try

        {

            _modalWindow = new MultiItemModalWindow(this, result, onDismissed: () =>

            {

                _modalWindow = null;

                _ = FocusScanBarAsync();

            });

            _modalWindow.Show();

        }

        catch (Exception ex)

        {

            AppLog.Error("PackingWindow: Failed to show MultiItemModalWindow", ex);

        }

    }



    private void CloseModal()

    {

        if (_modalWindow != null)

        {

            try

            {

                _modalWindow.Close();

            }

            catch

            {

                // ignore

            }

            _modalWindow = null;

        }

    }



    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)

    {

        if ((e.Key is Key.Escape or Key.Enter or Key.Space) && _modalWindow != null)

        {

            CloseModal();

            e.Handled = true;

        }

    }



    public async Task FocusScanBarAsync()

    {

        try

        {

            if (_coreReady && WebView.CoreWebView2 != null)

            {

                await WebView.ExecuteScriptAsync(VmsScripts.FocusAwbInput());

            }

        }

        catch (Exception ex)

        {

            AppLog.Info($"PackingWindow: FocusScanBarAsync: {ex.Message}");

        }

    }



    // ---- In-App Update Flow ------------------------------------------------



    private bool _hasPromptedUpdate;



    private void OnUpdateAvailable(ReleaseInfo release)

    {

        _announcedUpdate = release;

        PackingUpdateButton.Content = $"⚡ Update Available — v{release.Version}";

        PackingUpdateButton.Visibility = Visibility.Visible;

    }



    private void OnUpdateReadyToInstall(ReleaseInfo release, string setupPath)

    {

        _announcedUpdate = release;

        _downloadedVersion = release.Version;

        PackingUpdateButton.Content = $"⚡ Install Update v{release.Version} (Ready)";

        PackingUpdateButton.Visibility = Visibility.Visible;

        PackingUpdateButton.IsEnabled = true;



        // When the app opens or is idle (not actively recording/packing),

        // automatically prompt the operator once so they can update immediately with 1 click.

        if (!_hasPromptedUpdate && !_automation.IsPackingActive)

        {

            _hasPromptedUpdate = true;

            var message = $"A new update (v{release.Version}) is downloaded and ready to install." +

                          Environment.NewLine + Environment.NewLine +

                          "Install now? The desk will restart to complete the update.";

            var res = MessageBox.Show(

                this,

                message,

                $"Update Ready — v{release.Version}",

                MessageBoxButton.YesNo,

                MessageBoxImage.Information);



            if (res == MessageBoxResult.Yes)

            {

                UpdateChecker.LaunchInstallerAndExit(setupPath);

            }

        }

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

            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required --use-fake-ui-for-media-stream --disable-background-timer-throttling --disable-backgrounding-occluded-windows --disable-renderer-backgrounding --js-flags=--max-old-space-size=512 --disable-features=CalculateNativeWinOcclusion"

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

            if (args.PermissionKind is CoreWebView2PermissionKind.Camera

                or CoreWebView2PermissionKind.Microphone)

            {

                args.State = CoreWebView2PermissionState.Allow;

                return;

            }



            if (IsVmsOrigin(args.Uri))

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

                // CoreWebView2WebErrorStatus.ConnectionAborted occurs routinely during

                // client-side SPA routing and hash navigation (e.g. window.location.hash = '#/login').

                // It is benign and should never falsely alarm the operator.

                if (args.WebErrorStatus != CoreWebView2WebErrorStatus.ConnectionAborted)

                {

                    StatusText.Text = $"Navigation failed ({args.WebErrorStatus}). Check the VMS URL or network.";

                    RestartButton.Visibility = Visibility.Visible;

                }

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

            _ = FocusScanBarAsync();

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



    private async void RestartSession()

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



        // Purge any zombie / expired token so manual restart forces a clean login

        try

        {

            if (_coreReady && WebView.CoreWebView2 is not null)

            {

                await WebView.CoreWebView2.ExecuteScriptAsync("""

                    try {

                        localStorage.removeItem('fkuser');

                        localStorage.removeItem('__youthnic_fkuser_backup');

                        sessionStorage.clear();

                    } catch (e) {}

                """);

            }

        }

        catch { /* best effort */ }



        if (_coreReady && Uri.TryCreate(_config.VmsUrl, UriKind.Absolute, out var uri))

        {

            WebView.Source = uri;

        }

    }



    private void RestartButton_Click(object sender, RoutedEventArgs e) => RestartSession();



        private void SwitchDeskButton_Click(object sender, RoutedEventArgs e)

    {

        IsSwitchingDesk = true;

        Close();

    }



    private void ExitAppButton_Click(object sender, RoutedEventArgs e)

    {

        IsSwitchingDesk = false;

        Close();

    }



    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();



    private void OnUploadStatusChanged(int pendingCount, int errorCount)

    {

        Dispatcher.BeginInvoke(() =>

        {

            if (pendingCount == 0 && errorCount == 0)

            {

                UploadStatusText.Text = "● Uploads Clean (0)";

                UploadStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x34, 0xD3, 0x99));

                UploadStatusText.ToolTip = "All recorded packing videos have successfully uploaded to cloud.";

            }

            else if (pendingCount > 0 && errorCount == 0)

            {

                UploadStatusText.Text = $"⏳ {pendingCount} Uploading… DO NOT CLOSE";

                UploadStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0x9E, 0x0B));

                UploadStatusText.ToolTip = $"{pendingCount} packing video(s) are uploading. Keep application open!";

            }

            else if (errorCount > 0)

            {

                UploadStatusText.Text = $"🚨 {errorCount} Stuck Error(s) — Click Mass Retry!";

                UploadStatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xEF, 0x44, 0x44));

                UploadStatusText.ToolTip = $"{errorCount} video upload(s) failed/stuck. Open Error Requests tab in VMS and click 'Mass Retry'!";

            }

        });

    }



    private void Window_Closing(object? sender, CancelEventArgs e)

    {

        _isClosing = true;

        var pending = _automation.PendingRequestsCount;

        var errors = _automation.ErrorRequestsCount;



        // Hard safety guard: block accidental closing when recorded videos are still uploading or stuck in error

        if (pending > 0 || errors > 0)

        {

            var warningMessage = $"""
CRITICAL WARNING: RECORDED PACKING VIDEOS STILL IN VMS!

• Pending Uploads: {pending}
• Stuck / Error Videos: {errors}

Closing the application right now will interrupt or abort video uploads!
Without recorded video proof, marketplace customer return claims and weight dispute claims will be REJECTED.

What you must do:
1. If Pending > 0: Wait a few seconds until uploads finish and counter reaches 0.
2. If Error > 0: In the VMS panel on the right, click 'Mass Retry' to upload stuck videos.

Are you SURE you want to FORCE CLOSE anyway? (Not recommended)
""";

            var result = MessageBox.Show(

                this,

                warningMessage,

                "DO NOT CLOSE — Stuck/Pending Videos in VMS",

                MessageBoxButton.YesNo,

                MessageBoxImage.Warning);



            if (result != MessageBoxResult.Yes)

            {

                _isClosing = false;

                e.Cancel = true;

                return;

            }



            AppLog.Warn($"Operator manually force-closed desk with {pending} pending and {errors} error requests stuck!");

        }

        else if (_automation.IsPackingActive)

        {

            var result = MessageBox.Show(

                this,

                "An active packing session is running. Close anyway?",

                "Confirm close",

                MessageBoxButton.YesNo,

                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)

            {

                _isClosing = false;

                e.Cancel = true;

                return;

            }

        }



        _downloadCts?.Cancel();

        CloseModal();

        _maintenanceTimer?.Stop();

        _updateChecker.Stop();

        _updateChecker.Dispose();

        _coordinator.CancelPending();

        _automation.Stop();

        _automation.Dispose();

        _apiClient.Dispose();



        try

        {

            WebView.Dispose();

        }

        catch { }

    }



    private void PerformMaintenance()

    {

        try

        {

            GC.Collect(2, GCCollectionMode.Optimized);

        }

        catch (Exception ex)

        {

            AppLog.Info($"PackingWindow: shift maintenance warning: {ex.Message}");

        }

    }



    private void SettingsButton_Click(object sender, RoutedEventArgs e)

    {

        var wizard = new SetupWizard(_options) { Owner = this };

        if (wizard.ShowDialog() == true)

        {

            var oldVmsUrl = _config.VmsUrl;

            _options = SecureConfigStore.Load();

            _config = _options.For(_marketplace);

            _automation.UpdateConfig(_config);

            Title = $"Youthnic Packing Desk — {_marketplace} ({_options.DeskName})  ·  v{UpdateChecker.CurrentVersion()}";

            StatusText.Text = $"Settings updated: {_options.DeskName} (User: {_config.Username}, Feed: {_config.FeedId})";



            if (!string.Equals(oldVmsUrl, _config.VmsUrl, StringComparison.OrdinalIgnoreCase) &&

                _coreReady && Uri.TryCreate(_config.VmsUrl, UriKind.Absolute, out var newUri))

            {

                WebView.CoreWebView2.Navigate(newUri.AbsoluteUri);

            }

        }

    }



    public static string? ResolveDesktopSellerParameter(Marketplace marketplace, string? sellerAccount)

    {

        if (string.IsNullOrWhiteSpace(sellerAccount))

            return null;



        var trimmed = sellerAccount.Trim();



        // For Myntra desks configured with default seller "ETHNIC JUNCTION - Myntra Youthnic" (or "VB EXPORT - Myntra PPMP"),

        // passing null allows both local and hosted Lookup APIs to automatically query across BOTH Myntra accounts.

        if (marketplace == Marketplace.Myntra &&

            (string.Equals(trimmed, DeskOptions.DefaultMyntraSeller, StringComparison.OrdinalIgnoreCase) ||

             string.Equals(trimmed, DeskOptions.SecondaryMyntraSeller, StringComparison.OrdinalIgnoreCase)))

        {

            return null;

        }



        return trimmed;

    }



    private void ShowFatal(string message)

    {

        StateText.Text = message;

        MessageBox.Show(this, message, "Packing Desk", MessageBoxButton.OK, MessageBoxImage.Error);

    }

}

