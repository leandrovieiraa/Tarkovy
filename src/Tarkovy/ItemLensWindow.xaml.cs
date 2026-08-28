using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class ItemLensWindow : Window
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };

    public ItemLensWindow()
    {
        InitializeComponent();
        NativeMethods.EnableWorkAreaMaximize(this);
        WindowPlacementHelper.Wire(this, App.Settings.ItemLensWindowPlacement, () => SettingsStore.Save(App.Settings));
        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            WindowPlacementHelper.Restore(this, App.Settings.ItemLensWindowPlacement, Width, Height, wa.Left + 16, wa.Top + 16);
            WindowPlacementHelper.EnsureVisible(this);
            ApplyOpacity();
        };
    }

    public void ApplyOpacity() => Opacity = Math.Clamp(App.Settings.ItemLensOpacity, 0.5, 1.0);

    public void SetStatus(string text)
    {
        Dispatcher.Invoke(() => StatusText.Text = text);
    }

    public void ShowEmpty(string? hint = null)
    {
        Dispatcher.Invoke(() =>
        {
            ResultPanel.Visibility = Visibility.Collapsed;
            EmptyPanel.Visibility = Visibility.Visible;
            if (!string.IsNullOrWhiteSpace(hint))
                ((System.Windows.Controls.TextBlock)EmptyPanel.Children[0]).Text = hint;
        });
    }

    public void ShowResult(ItemScanResult result)
    {
        Dispatcher.Invoke(() =>
        {
            EmptyPanel.Visibility = Visibility.Collapsed;
            ResultPanel.Visibility = Visibility.Visible;
            var item = result.Item;
            ItemName.Text = item.Name;
            ItemShort.Text = item.ShortName;
            FleaPrice.Text = item.Avg24hPrice is > 0
                ? Loc.T("ItemLens.Flea", FormatRub(item.Avg24hPrice.Value))
                : Loc.T("ItemLens.FleaUnknown");
            var trader = item.BestTraderRub;
            TraderPrice.Text = trader > 0
                ? Loc.T("ItemLens.Trader", FormatRub(trader))
                : Loc.T("ItemLens.TraderUnknown");
            var perSlot = trader > 0 ? trader / item.Slots :
                item.Avg24hPrice is > 0 ? item.Avg24hPrice.Value / item.Slots : 0;
            SlotPrice.Text = perSlot > 0
                ? Loc.T("ItemLens.PerSlot", FormatRub(perSlot))
                : "";
            var badges = new List<string>();
            if (item.IsQuestItem) badges.Add(Loc.T("ItemLens.Badge.Quest"));
            if (item.IsHideoutItem) badges.Add(Loc.T("ItemLens.Badge.Hideout"));
            Badges.Text = badges.Count > 0 ? string.Join("  ·  ", badges) : "";
            _ = LoadIconAsync(item.IconLink ?? item.GridImageLink);
            PlaceNear(result.ScreenX, result.ScreenY);
        });
    }

    private async Task LoadIconAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            var bytes = await Http.GetByteArrayAsync(url).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                using var ms = new MemoryStream(bytes);
                var img = new BitmapImage();
                img.BeginInit();
                img.CacheOption = BitmapCacheOption.OnLoad;
                img.StreamSource = ms;
                img.EndInit();
                img.Freeze();
                ItemIcon.Source = img;
            });
        }
        catch { /* ignore */ }
    }

    private void PlaceNear(int x, int y)
    {
        var wa = SystemParameters.WorkArea;
        Left = Math.Clamp(x + 16, wa.Left, wa.Right - Width);
        Top = Math.Clamp(y + 16, wa.Top, wa.Bottom - ActualHeight);
    }

    private static string FormatRub(long v) => v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " ₽";

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); }
            catch { /* ignore */ }
        }
    }
}
