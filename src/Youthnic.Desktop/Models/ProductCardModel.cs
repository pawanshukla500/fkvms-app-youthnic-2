using System.ComponentModel;
using System.Net;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Youthnic.Desktop.Models;

public sealed class ProductCardModel : INotifyPropertyChanged
{
    private BitmapImage? _image;
    private bool _hasImage;

    public int Index { get; init; }
    public string Sku { get; init; } = string.Empty;
    public string SkuName { get; init; } = string.Empty;
    public int Quantity { get; init; } = 1;
    public string? Size { get; init; }
    public string? Seller { get; init; }
    public string? ImageUrl { get; init; }

    public BitmapImage? Image
    {
        get => _image;
        set
        {
            if (_image != value)
            {
                _image = value;
                HasImage = value != null;
                OnPropertyChanged();
            }
        }
    }

    public bool HasImage
    {
        get => _hasImage;
        private set
        {
            if (_hasImage != value)
            {
                _hasImage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsLoadingImage));
            }
        }
    }

    public bool IsLoadingImage => !_hasImage;

    public static ProductCardModel FromLookupItem(LookupItem item, int index)
    {
        var rawSku = (item.Sku ?? string.Empty).Trim();
        var rawName = (item.SkuName ?? string.Empty).Trim();
        var decodedName = WebUtility.HtmlDecode(rawName);
        var size = ExtractSize(rawSku);

        return new ProductCardModel
        {
            Index = index,
            Sku = rawSku,
            SkuName = string.IsNullOrWhiteSpace(decodedName) ? rawSku : decodedName,
            Quantity = Math.Max(1, item.Quantity),
            Size = size,
            Seller = WebUtility.HtmlDecode(item.Seller ?? string.Empty),
            ImageUrl = item.ImageUrls?.FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
        };
    }

    public static string? ExtractSize(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return null;
        var s = sku.Trim();
        var lastUnderscore = s.LastIndexOf('_');
        if (lastUnderscore > 0 && lastUnderscore < s.Length - 1)
        {
            var suffix = s[(lastUnderscore + 1)..].Trim().ToUpperInvariant();
            if (IsKnownSize(suffix)) return suffix;
        }
        var lastHyphen = s.LastIndexOf('-');
        if (lastHyphen > 0 && lastHyphen < s.Length - 1)
        {
            var suffix = s[(lastHyphen + 1)..].Trim().ToUpperInvariant();
            if (IsKnownSize(suffix)) return suffix;
        }
        return null;
    }

    public static bool IsKnownSize(string s) => s switch
    {
        "XS" or "S" or "M" or "L" or "XL" or "XXL" or "2XL" or "3XL" or "4XL" or "5XL" or "6XL" or "FREE" or "FS" => true,
        _ => false
    };

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
