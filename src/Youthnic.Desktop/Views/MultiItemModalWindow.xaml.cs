using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Youthnic.Desktop.Models;
using Youthnic.Desktop.Services;

namespace Youthnic.Desktop.Views;

public partial class MultiItemModalWindow : Window
{
    private readonly ObservableCollection<ProductCardModel> _cards = new();
    private readonly DispatcherTimer _timer;
    private readonly Action? _onDismissed;
    private int _countdown = 6;
    private bool _isClosing;

    public MultiItemModalWindow(Window owner, ScanLookupResult result, Action? onDismissed = null)
    {
        Owner = owner;
        _onDismissed = onDismissed;

        InitializeComponent();

        Opacity = 0.0;
        Loaded += (_, _) =>
        {
            var fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(150)));
            BeginAnimation(OpacityProperty, fadeIn);
        };

        ProductCardsItemsControl.ItemsSource = _cards;

        var totalUnits = result.Items.Sum(i => i.Quantity);
        BannerTitle.Text = $"MULTI-ITEM PARCEL — PACK ALL {totalUnits} ITEMS";
        BannerSubtitle.Text = $"{totalUnits} items · {result.Items.Count} different SKUs · {result.Awb}";

        for (var i = 0; i < result.Items.Count; i++)
        {
            _cards.Add(ProductCardModel.FromLookupItem(result.Items[i], i));
        }

        // Fit exactly over owner window coordinates
        if (owner.ActualWidth > 0 && owner.ActualHeight > 0)
        {
            Left = owner.Left;
            Top = owner.Top;
            Width = owner.ActualWidth;
            Height = owner.ActualHeight;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Width = 980;
            Height = 620;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _countdown--;
            if (_countdown <= 0)
            {
                SafeClose();
            }
            else
            {
                CountdownText.Text = $"Closes in {_countdown}s · press Enter, Esc, or click outside to dismiss";
            }
        };
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            _onDismissed?.Invoke();
        };
    }

    public void SetItemImage(int index, BitmapImage image)
    {
        if (index >= 0 && index < _cards.Count)
        {
            _cards[index].Image = image;
        }
    }

    private void SafeClose()
    {
        if (_isClosing) return;
        _isClosing = true;
        _timer.Stop();
        if (IsLoaded)
        {
            var fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(150)));
            fadeOut.Completed += (_, _) =>
            {
                try { Close(); } catch { }
            };
            BeginAnimation(OpacityProperty, fadeOut);
        }
        else
        {
            try { Close(); } catch { }
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => SafeClose();

    private void CardContent_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Prevent clicking inside the card content from closing the modal
        e.Handled = true;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        // Clicking on the dim backdrop closes the modal
        SafeClose();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter or Key.Space)
        {
            e.Handled = true;
            SafeClose();
        }
    }
}
