using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Views;

public partial class SetupWizard : Window
{
    private static readonly string[] StepTitles =
    {
        "Device — identity of this packing desk",
        "Flipkart — VMS connection (required)",
        "Myntra — VMS connection (optional)",
        "Lookup API — local SKU lookup service",
        "Finish — review and save"
    };

    private readonly LookupApiClient _apiClient = new();
    private int _step;

    public SetupWizard(DeskOptions? existing)
    {
        InitializeComponent();

        var options = existing ?? DeskOptions.Empty;
        var defaults = existing is null ? InstallDefaults.Load() : InstallDefaults.Empty;

        DeviceIdBox.Text = string.IsNullOrWhiteSpace(options.DeviceId)
            ? Guid.NewGuid().ToString("N")
            : options.DeviceId;
        DeskNameBox.Text = !string.IsNullOrWhiteSpace(options.DeskName) ? options.DeskName : "Packing Desk";
        LookupUrlBox.Text = !string.IsNullOrWhiteSpace(options.LookupApiUrl)
            ? options.LookupApiUrl
            : !string.IsNullOrWhiteSpace(defaults.LookupApiUrl)
                ? defaults.LookupApiUrl!
                : DeskOptions.DefaultLookupApiUrl;
        LookupKeyBox.Text = !string.IsNullOrWhiteSpace(options.LookupApiKey)
            ? options.LookupApiKey
            : !string.IsNullOrWhiteSpace(defaults.LookupApiKey)
                ? defaults.LookupApiKey!
                : DeskOptions.DefaultLookupApiKey;

        FkUrlBox.Text = !string.IsNullOrWhiteSpace(options.Flipkart.VmsUrl)
            ? options.Flipkart.VmsUrl
            : !string.IsNullOrWhiteSpace(defaults.FlipkartVmsUrl)
                ? defaults.FlipkartVmsUrl!
                : DeskOptions.DefaultFlipkartVmsUrl;
        FkUserBox.Text = !string.IsNullOrWhiteSpace(options.Flipkart.Username)
            ? options.Flipkart.Username
            : !string.IsNullOrWhiteSpace(defaults.FlipkartUsername)
                ? defaults.FlipkartUsername!
                : DeskOptions.DefaultFlipkartUsername;
        FkPassBox.Password = !string.IsNullOrWhiteSpace(options.Flipkart.Password)
            ? options.Flipkart.Password
            : !string.IsNullOrWhiteSpace(defaults.FlipkartPassword)
                ? defaults.FlipkartPassword!
                : DeskOptions.DefaultFlipkartPassword;
        FkFeedBox.Text = !string.IsNullOrWhiteSpace(options.Flipkart.FeedId)
            ? options.Flipkart.FeedId
            : !string.IsNullOrWhiteSpace(defaults.FlipkartFeedId)
                ? defaults.FlipkartFeedId!
                : DeskOptions.DefaultFlipkartFeedId;
        FkSellerBox.Text = !string.IsNullOrWhiteSpace(options.Flipkart.SellerAccount)
            ? options.Flipkart.SellerAccount
            : !string.IsNullOrWhiteSpace(defaults.FlipkartSeller)
                ? defaults.FlipkartSeller!
                : DeskOptions.DefaultFlipkartSeller;

        MyUrlBox.Text = !string.IsNullOrWhiteSpace(options.Myntra.VmsUrl)
            ? options.Myntra.VmsUrl
            : !string.IsNullOrWhiteSpace(defaults.MyntraVmsUrl)
                ? defaults.MyntraVmsUrl!
                : DeskOptions.DefaultMyntraVmsUrl;
        MyUserBox.Text = !string.IsNullOrWhiteSpace(options.Myntra.Username)
            ? options.Myntra.Username
            : !string.IsNullOrWhiteSpace(defaults.MyntraUsername)
                ? defaults.MyntraUsername!
                : DeskOptions.DefaultMyntraUsername;
        MyPassBox.Password = !string.IsNullOrWhiteSpace(options.Myntra.Password)
            ? options.Myntra.Password
            : !string.IsNullOrWhiteSpace(defaults.MyntraPassword)
                ? defaults.MyntraPassword!
                : DeskOptions.DefaultMyntraPassword;
        MyFeedBox.Text = !string.IsNullOrWhiteSpace(options.Myntra.FeedId)
            ? options.Myntra.FeedId
            : !string.IsNullOrWhiteSpace(defaults.MyntraFeedId)
                ? defaults.MyntraFeedId!
                : DeskOptions.DefaultMyntraFeedId;
        MySellerBox.Text = !string.IsNullOrWhiteSpace(options.Myntra.SellerAccount)
            ? options.Myntra.SellerAccount
            : !string.IsNullOrWhiteSpace(defaults.MyntraSeller)
                ? defaults.MyntraSeller!
                : DeskOptions.DefaultMyntraSeller;

        ShowStep(0);
    }

    private void ShowStep(int step)
    {
        _step = step;
        StepTitle.Text = $"Step {step + 1} of {StepTitles.Length} — {StepTitles[step]}";
        DevicePage.Visibility = step == 0 ? Visibility.Visible : Visibility.Collapsed;
        FlipkartPage.Visibility = step == 1 ? Visibility.Visible : Visibility.Collapsed;
        MyntraPage.Visibility = step == 2 ? Visibility.Visible : Visibility.Collapsed;
        LookupPage.Visibility = step == 3 ? Visibility.Visible : Visibility.Collapsed;
        FinishPage.Visibility = step == 4 ? Visibility.Visible : Visibility.Collapsed;

        BackButton.IsEnabled = step > 0;
        NextButton.Content = step == StepTitles.Length - 1 ? "Save" : "Next";

        if (step == 4) RenderSummary();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => ShowStep(_step - 1);

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateStep(_step)) return;

        if (_step == StepTitles.Length - 1)
        {
            Save();
            return;
        }

        ShowStep(_step + 1);
    }

    private bool ValidateStep(int step)
    {
        switch (step)
        {
            case 0:
                if (string.IsNullOrWhiteSpace(DeviceIdBox.Text))
                {
                    return Fail("Device ID must not be empty.", DeviceIdBox);
                }
                return true;

            case 1:
                if (string.IsNullOrWhiteSpace(FkUrlBox.Text) ||
                    !Uri.TryCreate(FkUrlBox.Text.Trim(), UriKind.Absolute, out _))
                {
                    return Fail("Enter the Flipkart VMS URL (https://…).", FkUrlBox);
                }
                if (string.IsNullOrWhiteSpace(FkUserBox.Text)) return Fail("Enter the Flipkart VMS username.", FkUserBox);
                if (FkPassBox.Password.Length == 0) return Fail("Enter the Flipkart VMS password.", FkPassBox);
                if (string.IsNullOrWhiteSpace(FkFeedBox.Text)) return Fail("Enter the Flipkart feed id.", FkFeedBox);
                return true;

            case 2:
                // Myntra is optional: a blank URL means "skip".
                if (string.IsNullOrWhiteSpace(MyUrlBox.Text)) return true;
                if (!Uri.TryCreate(MyUrlBox.Text.Trim(), UriKind.Absolute, out _))
                {
                    return Fail("Enter a valid Myntra VMS URL, or leave it empty to skip.", MyUrlBox);
                }
                if (string.IsNullOrWhiteSpace(MyUserBox.Text)) return Fail("Enter the Myntra VMS username.", MyUserBox);
                if (MyPassBox.Password.Length == 0) return Fail("Enter the Myntra VMS password.", MyPassBox);
                if (string.IsNullOrWhiteSpace(MyFeedBox.Text)) return Fail("Enter the Myntra feed id.", MyFeedBox);
                return true;

            case 3:
                if (string.IsNullOrWhiteSpace(LookupUrlBox.Text) ||
                    !Uri.TryCreate(LookupUrlBox.Text.Trim(), UriKind.Absolute, out _))
                {
                    return Fail("Enter the lookup API base URL.", LookupUrlBox);
                }
                return true;

            default:
                return true;
        }
    }

    private static bool Fail(string message, Control target)
    {
        MessageBox.Show(message, "Setup", MessageBoxButton.OK, MessageBoxImage.Warning);
        target.Focus();
        return false;
    }

    private async void TestConnection_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(LookupUrlBox.Text) ||
            !Uri.TryCreate(LookupUrlBox.Text.Trim(), UriKind.Absolute, out _))
        {
            TestResultText.Text = "Enter a valid URL first.";
            TestResultText.Foreground = Brushes.IndianRed;
            return;
        }

        TestConnectionButton.IsEnabled = false;
        TestResultText.Text = "Testing…";
        TestResultText.Foreground = (Brush)FindResource("TextMuted");
        try
        {
            var ok = await _apiClient.TestHealthAsync(LookupUrlBox.Text, LookupKeyBox.Text, CancellationToken.None);
            TestResultText.Text = ok ? "Connection OK" : "No response — check the service is running.";
            TestResultText.Foreground = ok ? Brushes.SeaGreen : Brushes.IndianRed;
        }
        catch (Exception ex)
        {
            TestResultText.Text = $"Failed: {ex.Message}";
            TestResultText.Foreground = Brushes.IndianRed;
        }
        finally
        {
            TestConnectionButton.IsEnabled = true;
        }
    }

    private void RenderSummary()
    {
        var myntraSkipped = string.IsNullOrWhiteSpace(MyUrlBox.Text);
        SummaryText.Text =
            $"Device: {DeviceIdBox.Text} ({DeskNameBox.Text})" + Environment.NewLine +
            $"Flipkart: {FkUrlBox.Text}, user {FkUserBox.Text}, feed {FkFeedBox.Text}" + Environment.NewLine +
            (myntraSkipped
                ? "Myntra: skipped (not configured on this desk)"
                : $"Myntra: {MyUrlBox.Text}, user {MyUserBox.Text}, feed {MyFeedBox.Text}") + Environment.NewLine +
            $"Lookup API: {LookupUrlBox.Text}" +
            (string.IsNullOrWhiteSpace(LookupKeyBox.Text) ? " (no API key)" : " (API key set)");
    }

    private void Save()
    {
        var myntraSkipped = string.IsNullOrWhiteSpace(MyUrlBox.Text);
        var options = new DeskOptions(
            DeviceId: DeviceIdBox.Text.Trim(),
            DeskName: DeskNameBox.Text.Trim(),
            LookupApiUrl: LookupUrlBox.Text.Trim(),
            LookupApiKey: LookupKeyBox.Text.Trim(),
            Flipkart: new MarketplaceConfig(
                FkUrlBox.Text.Trim(),
                FkUserBox.Text.Trim(),
                FkPassBox.Password,
                FkFeedBox.Text.Trim(),
                FkSellerBox.Text.Trim()),
            Myntra: myntraSkipped
                ? new MarketplaceConfig(string.Empty, string.Empty, string.Empty, string.Empty)
                : new MarketplaceConfig(
                    MyUrlBox.Text.Trim(),
                    MyUserBox.Text.Trim(),
                    MyPassBox.Password,
                    MyFeedBox.Text.Trim(),
                    MySellerBox.Text.Trim()),
            SetupCompleted: true);

        try
        {
            SecureConfigStore.Save(options);
            AppLog.Info("Setup wizard: configuration saved");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            AppLog.Error("Setup wizard: save failed", ex);
            MessageBox.Show(
                $"Could not save configuration: {ex.Message}",
                "Setup", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _apiClient.Dispose();
        base.OnClosed(e);
    }
}
