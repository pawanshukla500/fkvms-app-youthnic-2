using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Views;

/// <summary>Display row for the "also in this shipment" list. Text is HTML-decoded
/// (the lookup API can return entities like &amp;amp;) before it reaches the panel.</summary>
public sealed record AdditionalItemRow(int Quantity, string Sku, string SkuName);

public partial class SkuPanel : UserControl
{
    public event Action? RetryRequested;

    private long _currentSeq;

    public SkuPanel()
    {
        InitializeComponent();
        ShowIdle();
    }

    public void ShowIdle()
    {
        SetState(IdlePanel);
        SkuNameText.Text = string.Empty;
        SkuIdText.Text = string.Empty;
        AdditionalItemsList.ItemsSource = null;
        StatusText.Text = "Scan an AWB in the VMS to see the product here.";
    }

    /// <summary>Newest scan wins: a loading spinner only ever belongs to the latest scan.</summary>
    public void ShowLoading(long seq, string awb)
    {
        _currentSeq = seq;
        SetState(LoadingPanel);
        LoadingText.Text = $"Looking up {awb}…";
        StatusText.Text = string.Empty;
    }

    public void ShowResult(ScanLookupResult result)
    {
        if (result.Seq != _currentSeq) return; // stale result from a cancelled scan

        switch (result.Outcome)
        {
            case LookupOutcome.Ok when result.Items.Count > 0:
                ShowProduct(result);
                break;

            case LookupOutcome.NotFound:
                ShowMessage("✕", "SKU not found",
                    FirstNonEmpty(result.Hint, "This AWB has no mapping yet.") +
                    (HasPendingSyncHint(result) ? " Pending sync — retry shortly." : string.Empty),
                    showRetry: true);
                StatusText.Text = $"AWB {result.Awb}: not found";
                break;

            case LookupOutcome.MappingPending:
                ShowMessage("…", "Mapping pending",
                    FirstNonEmpty(result.Hint, "Product mapping is pending sync — retry shortly."),
                    showRetry: true);
                StatusText.Text = $"AWB {result.Awb}: mapping pending";
                break;

            case LookupOutcome.Error:
                ShowMessage("!", "Lookup failed",
                    FirstNonEmpty(result.Hint, "The lookup service did not respond. The VMS is unaffected — retry."),
                    showRetry: true);
                StatusText.Text = $"AWB {result.Awb}: lookup error";
                break;

            default:
                ShowMessage("?", "No items",
                    "The lookup returned no items for this AWB.", showRetry: true);
                break;
        }
    }

    private void ShowProduct(ScanLookupResult result)
    {
        var main = result.Items[0];
        // The lookup payload may carry HTML entities (e.g. "&amp;") — decode before display.
        SkuNameText.Text = Decode(main.SkuName);
        SkuIdText.Text = $"SKU {main.Sku}" +
                         (main.Quantity > 1 ? $"  ·  Qty {main.Quantity}" : string.Empty) +
                         (string.IsNullOrWhiteSpace(main.Seller)
                             ? string.Empty
                             : $"  ·  {Decode(main.Seller)}");

        // Image arrives asynchronously after the result is published; until then the
        // "No image" placeholder stays up with the names/qty already visible.
        ProductImage.Source = null;
        ProductImage.Visibility = Visibility.Collapsed;
        ImageMissingOverlay.Visibility = Visibility.Visible;

        AdditionalItemsList.ItemsSource = result.Items.Skip(1)
            .Select(i => new AdditionalItemRow(i.Quantity, i.Sku, Decode(i.SkuName)))
            .ToList();
        SetState(ImageMissingOverlay);
        StatusText.Text = $"AWB {result.Awb} — {result.Items.Count} item(s)";
    }

    /// <summary>
    /// Called when the first item's image has finished downloading. Guarded by the
    /// scan sequence: a bitmap from an older scan never replaces a newer scan's panel.
    /// </summary>
    public void ShowImage(long seq, BitmapImage image)
    {
        if (seq != _currentSeq) return; // stale image from a cancelled/superseded scan

        ProductImage.Source = image;
        ProductImage.Visibility = Visibility.Visible;
        ImageMissingOverlay.Visibility = Visibility.Collapsed;
        SetState(ProductImage);
    }

    private void ShowMessage(string glyph, string title, string hint, bool showRetry)
    {
        SkuNameText.Text = string.Empty;
        SkuIdText.Text = string.Empty;
        AdditionalItemsList.ItemsSource = null;
        ProductImage.Source = null;
        ProductImage.Visibility = Visibility.Collapsed;
        ImageMissingOverlay.Visibility = Visibility.Collapsed;

        MessageGlyph.Text = glyph;
        MessageTitle.Text = title;
        MessageHint.Text = hint;
        RetryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        SetState(MessagePanel);
    }

    private void SetState(UIElement active)
    {
        IdlePanel.Visibility = Equals(active, IdlePanel) ? Visibility.Visible : Visibility.Collapsed;
        LoadingPanel.Visibility = Equals(active, LoadingPanel) ? Visibility.Visible : Visibility.Collapsed;
        MessagePanel.Visibility = Equals(active, MessagePanel) ? Visibility.Visible : Visibility.Collapsed;
        ProductImage.Visibility = Equals(active, ProductImage) ? Visibility.Visible : Visibility.Collapsed;
        ImageMissingOverlay.Visibility = Equals(active, ImageMissingOverlay) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();

    private static string FirstNonEmpty(string? preferred, string fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;

    private static string Decode(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : WebUtility.HtmlDecode(text);

    private static bool HasPendingSyncHint(ScanLookupResult result) =>
        !string.IsNullOrWhiteSpace(result.Hint) &&
        result.Hint.Contains("sync", StringComparison.OrdinalIgnoreCase);
}
