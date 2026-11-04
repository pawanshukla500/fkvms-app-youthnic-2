using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Views;

public partial class SelectionWindow : Window
{
    private const int ReleaseNotesPreviewChars = 800;

    private DeskOptions? _options;
    private readonly UpdateChecker _updateChecker;
    private ReleaseInfo? _announcedUpdate;
    private string? _downloadedVersion;
    private CancellationTokenSource? _downloadCts;

    public SelectionWindow()
    {
        InitializeComponent();

        _updateChecker = new UpdateChecker(Dispatcher);

        var version = UpdateChecker.CurrentVersion();
        VersionText.Text = $"v{version}";
        Title = $"Youthnic Packing Desk — Select Marketplace  ·  v{version}";
        AppLog.Info($"Youthnic Packing Desk starting — version {version}");

        _updateChecker.UpdateAvailable += OnUpdateAvailable;
        _updateChecker.UpdateReadyToInstall += OnUpdateReadyToInstall;

        Loaded += (_, _) =>
        {
            Refresh();
            _updateChecker.Start();
        };
        Closed += (_, _) =>
        {
            _downloadCts?.Cancel();
            _updateChecker.Stop();
            _updateChecker.Dispose();
        };
    }

    private void Refresh()
    {
        _options = SecureConfigStore.Load();
        ApplyCardState(FlipkartCard, FlipkartStatus, null, Marketplace.Flipkart);
        ApplyCardState(MyntraCard, MyntraStatus, MyntraHint, Marketplace.Myntra);
    }

    private void ApplyCardState(
        System.Windows.Controls.Border card,
        System.Windows.Controls.TextBlock status,
        System.Windows.Controls.TextBlock? hint,
        Marketplace marketplace)
    {
        var configured = _options?.For(marketplace).IsConfigured == true;
        status.Text = configured ? "Configured ✓" : "Configuration pending";
        status.Foreground = configured
            ? new SolidColorBrush(Color.FromRgb(0x1E, 0x8E, 0x3E))
            : (Brush)FindResource("TextMuted");

        if (hint is not null)
        {
            hint.Visibility = configured ? Visibility.Collapsed : Visibility.Visible;
        }

        // Both cards are fully interactive; clicking an unconfigured card opens SetupWizard
        card.Opacity = 1.0;
        card.Cursor = Cursors.Hand;
        card.Tag = marketplace;
    }

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Border card ||
            card.Tag is not Marketplace marketplace)
        {
            return;
        }

        var configured = _options?.For(marketplace).IsConfigured == true;

        if (!configured)
        {
            // Not configured → run the setup wizard first, then come back here.
            if (OpenWizard())
            {
                Refresh();
            }
            return;
        }

        OpenPacking(marketplace);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (OpenWizard())
        {
            Refresh();
        }
    }

    private bool OpenWizard()
    {
        var wizard = new SetupWizard(_options) { Owner = this };
        return wizard.ShowDialog() == true;
    }

    private void OpenPacking(Marketplace marketplace)
    {
        if (_options is null) return;
        var window = new PackingWindow(marketplace, _options) { Owner = this };
        window.Closed += (_, _) => Refresh();
        window.Show();
        Hide();
    }

    // ---- Update flow -------------------------------------------------------

    /// <summary>Raised on the UI dispatcher by UpdateChecker — safe to touch UI here.</summary>
    private bool _hasPromptedUpdate;

    private void OnUpdateAvailable(ReleaseInfo release)
    {
        _announcedUpdate = release;
        UpdateButton.Content = $"Update available — v{release.Version}";
        UpdateButton.Visibility = Visibility.Visible;
    }

    private void OnUpdateReadyToInstall(ReleaseInfo release, string setupPath)
    {
        _announcedUpdate = release;
        _downloadedVersion = release.Version;
        UpdateButton.Content = $"⚡ Install v{release.Version} (Ready)";
        UpdateButton.Visibility = Visibility.Visible;
        UpdateButton.IsEnabled = true;

        if (!_hasPromptedUpdate)
        {
            _hasPromptedUpdate = true;
            var message = $"A new update (v{release.Version}) has been downloaded and is ready to install." +
                          Environment.NewLine + Environment.NewLine +
                          "Install now before opening a packing desk?";
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

    /// <summary>Manual on-demand check (clicking the version label). Silent when up to date.</summary>
    private void VersionText_Click(object sender, MouseButtonEventArgs e)
    {
        VersionText.Text = $"v{UpdateChecker.CurrentVersion()} — checking…";
        _ = _updateChecker.CheckNowAsync(CancellationToken.None).ContinueWith(_ =>
            Dispatcher.BeginInvoke(() =>
                VersionText.Text = _announcedUpdate is null
                    ? $"v{UpdateChecker.CurrentVersion()}"
                    : $"v{UpdateChecker.CurrentVersion()} — update ready"),
            TaskScheduler.Default);
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_announcedUpdate is null) return;

        var notes = _announcedUpdate.Notes.Trim();
        if (notes.Length > ReleaseNotesPreviewChars)
            notes = notes[..ReleaseNotesPreviewChars] + "…";

        var message = $"Version {_announcedUpdate.Version} is available." +
                      Environment.NewLine + Environment.NewLine +
                      (notes.Length > 0 ? notes + Environment.NewLine + Environment.NewLine : string.Empty) +
                      "Download the installer now? (Downloaded from the official GitHub release over HTTPS.)";

        var confirm = MessageBox.Show(
            this, message, $"Update — v{_announcedUpdate.Version}",
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

        UpdateButton.IsEnabled = false;
        UpdateButton.Content = $"Downloading v{release.Version}…";
        DownloadProgressText.Visibility = Visibility.Visible;
        DownloadProgressText.Text = "Preparing download…";

        // Progress<double> callbacks arrive on the thread pool — marshal to the UI thread.
        var progress = new Progress<double>(fraction =>
            Dispatcher.BeginInvoke(() =>
                DownloadProgressText.Text = $"Downloading update… {fraction * 100:0}%"));

        try
        {
            // Already downloaded this exact version? Skip the network trip.
            if (_downloadedVersion == release.Version && File.Exists(destination)) { /* go straight to install prompt */ }
            else
            {
                await _updateChecker.DownloadAsync(release, destination, progress, token);
                _downloadedVersion = release.Version;
            }

            DownloadProgressText.Visibility = Visibility.Collapsed;

            var install = MessageBox.Show(
                this,
                $"The installer for v{release.Version} is ready." + Environment.NewLine + Environment.NewLine +
                "Install now? The app will close and the setup will install over the current version." +
                Environment.NewLine + "(Choose No to keep the file and install later.)",
                "Update ready",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (install == MessageBoxResult.Yes)
            {
                UpdateChecker.LaunchInstallerAndExit(destination);
            }
            else
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = $"Install v{release.Version} — ready";
            }
        }
        catch (OperationCanceledException)
        {
            DownloadProgressText.Visibility = Visibility.Collapsed;
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = $"Update available — v{release.Version}";
        }
        catch (Exception ex)
        {
            AppLog.Error("Update download failed", ex);
            DownloadProgressText.Visibility = Visibility.Collapsed;
            UpdateButton.IsEnabled = true;
            UpdateButton.Content = $"Update available — v{release.Version}";
            MessageBox.Show(
                this, $"The update could not be downloaded:{Environment.NewLine}{ex.Message}",
                "Update failed", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
