using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Views;

public partial class SkuPanel : UserControl
{
    public event Action? RetryRequested;

    private long _currentSeq;

    private DispatcherTimer? _wrongMpTimer;
    private int _wrongMpCountdown;

    public ObservableCollection<ProductCardModel> ProductCards { get; } = new();

    public SkuPanel()
    {
        InitializeComponent();
        MultiItemsGrid.ItemsSource = ProductCards;
        ShowIdle();
    }

    public void SetMarketplace(Marketplace marketplace)
    {
        if (marketplace == Marketplace.Flipkart)
        {
            MarketplaceText.Text = "Flipkart Desk";
            MarketplaceLogo.Source = new BitmapImage(new Uri("pack://application:,,,/Youthnic.Desktop;component/Assets/flipkart_icon.png", UriKind.Absolute));
        }
        else
        {
            MarketplaceText.Text = "Myntra Desk";
            MarketplaceLogo.Source = new BitmapImage(new Uri("pack://application:,,,/Youthnic.Desktop;component/Assets/myntra_logo.png", UriKind.Absolute));
        }
    }

    public void ShowIdle()
    {
        DismissAllPopups();
        ProductCards.Clear();
        SetState(IdlePanel);
        SkuNameText.Text = string.Empty;
        SkuIdText.Text = string.Empty;
        SingleItemDetails.Visibility = Visibility.Collapsed;
        StatusText.Text = "Scan an AWB in the VMS to see the product here.";
    }

    /// <summary>Newest scan wins: a loading spinner only ever belongs to the latest scan.</summary>
    public void ShowLoading(long seq, string awb)
    {
        _currentSeq = seq;
        DismissAllPopups();
        ProductCards.Clear();
        SetState(LoadingPanel);
        LoadingText.Text = $"Looking up {awb}…";
        SingleItemDetails.Visibility = Visibility.Collapsed;
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

            case LookupOutcome.WrongMarketplace:
                ShowMessage("🚫", "Wrong Marketplace Package",
                    FirstNonEmpty(result.Message, "This barcode was scanned at the wrong packing desk."),
                    showRetry: false);
                StatusText.Text = $"AWB {result.Awb}: {result.Message ?? "Wrong marketplace"}";
                ShowWrongMarketplaceAlert(result);
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
        ProductCards.Clear();
        for (var i = 0; i < result.Items.Count; i++)
        {
            var card = ProductCardModel.FromLookupItem(result.Items[i], i);
            ProductCards.Add(card);
        }

        var totalUnits = result.Items.Sum(i => i.Quantity);
        var isMulti = result.Items.Count > 1 || totalUnits > 1;

        if (isMulti)
        {
            MultiItemBannerText.Text = $"MULTI-ITEM: {totalUnits} units · {result.Items.Count} different SKUs";
            SingleItemPanel.Visibility = Visibility.Collapsed;
            SingleItemDetails.Visibility = Visibility.Collapsed;
            MultiItemsPanel.Visibility = Visibility.Visible;
            SetState(MultiItemsPanel);
        }
        else
        {
            var main = ProductCards[0];
            SkuNameText.Text = main.SkuName;
            SkuIdText.Text = $"SKU {main.Sku}" +
                             (main.Quantity > 1 ? $"  ·  Qty {main.Quantity}" : string.Empty) +
                             (string.IsNullOrWhiteSpace(main.Seller) ? string.Empty : $"  ·  {main.Seller}");

            if (!string.IsNullOrWhiteSpace(main.Size))
            {
                PlaceholderSizeText.Text = $"SIZE: {main.Size}";
                PlaceholderSizeBadge.Visibility = Visibility.Visible;
            }
            else
            {
                PlaceholderSizeBadge.Visibility = Visibility.Collapsed;
            }

            PlaceholderDesignText.Text = main.SkuName;
            ProductImage.Source = null;
            ProductImage.Visibility = Visibility.Collapsed;
            ImagePlaceholderCard.Visibility = Visibility.Visible;

            MultiItemsPanel.Visibility = Visibility.Collapsed;
            SingleItemPanel.Visibility = Visibility.Visible;
            SingleItemDetails.Visibility = Visibility.Visible;
            SetState(SingleItemPanel);
        }

        StatusText.Text = $"AWB {result.Awb} — {result.Items.Count} item(s), {totalUnits} unit(s)";
    }

    /// <summary>
    /// Updates image for an item by sequence and index. Stale sequences are rejected.
    /// </summary>
    public void SetItemImage(long seq, int index, BitmapImage image)
    {
        if (seq != _currentSeq) return;

        if (index >= 0 && index < ProductCards.Count)
        {
            ProductCards[index].Image = image;
        }

        if (index == 0)
        {
            ProductImage.Source = image;
            ProductImage.Visibility = Visibility.Visible;
            ImagePlaceholderCard.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Legacy compatibility for single item image updates.
    /// </summary>
    public void ShowImage(long seq, BitmapImage image)
    {
        SetItemImage(seq, 0, image);
    }

    private void ShowWrongMarketplaceAlert(ScanLookupResult result)
    {
        _wrongMpTimer?.Stop();
        _wrongMpCountdown = 4;
        WrongMarketplaceDesc.Text = !string.IsNullOrWhiteSpace(result.Message)
            ? result.Message
            : "This barcode belongs to another marketplace! Do not pack at this desk.";
        WrongMarketplaceCountdownText.Text = $"Alert closing in {_wrongMpCountdown}s…";
        WrongMarketplaceOverlay.Visibility = Visibility.Visible;

        _wrongMpTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _wrongMpTimer.Tick += (s, e) =>
        {
            _wrongMpCountdown--;
            if (_wrongMpCountdown <= 0)
            {
                _wrongMpTimer?.Stop();
                WrongMarketplaceOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                WrongMarketplaceCountdownText.Text = $"Alert closing in {_wrongMpCountdown}s…";
            }
        };
        _wrongMpTimer.Start();
    }

    private void DismissAllPopups()
    {
        _wrongMpTimer?.Stop();
        WrongMarketplaceOverlay.Visibility = Visibility.Collapsed;
    }

    private void DismissWrongMarketplace_Click(object sender, RoutedEventArgs e)
    {
        _wrongMpTimer?.Stop();
        WrongMarketplaceOverlay.Visibility = Visibility.Collapsed;
    }

    private void ShowMessage(string glyph, string title, string hint, bool showRetry)
    {
        ProductCards.Clear();
        SkuNameText.Text = string.Empty;
        SkuIdText.Text = string.Empty;
        SingleItemDetails.Visibility = Visibility.Collapsed;
        ProductImage.Source = null;
        ProductImage.Visibility = Visibility.Collapsed;
        ImagePlaceholderCard.Visibility = Visibility.Collapsed;
        SingleItemPanel.Visibility = Visibility.Collapsed;
        MultiItemsPanel.Visibility = Visibility.Collapsed;

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
        SingleItemPanel.Visibility = Equals(active, SingleItemPanel) ? Visibility.Visible : Visibility.Collapsed;
        MultiItemsPanel.Visibility = Equals(active, MultiItemsPanel) ? Visibility.Visible : Visibility.Collapsed;
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
