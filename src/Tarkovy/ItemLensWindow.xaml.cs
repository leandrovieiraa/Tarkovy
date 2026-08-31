using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class ItemLensWindow : Window
{
    private const double DefaultWidth = 340;
    private const double DefaultHeight = 360;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private bool _placementReady;
    private double _targetOpacity = 0.88;
    private bool _revealed;
    private bool _forceClose;
    private DateTime _loadingShownUtc;
    private Storyboard? _spinnerStoryboard;

    private static readonly SolidColorBrush LoadingHintBrush = Freeze(0xCC, 0xCC, 0xCC);
    private static readonly SolidColorBrush ReadyStatusBrush = Freeze(0x8A, 0x8A, 0x8A);
    private static readonly SolidColorBrush EmptyHintBrush = Freeze(0xAA, 0xAA, 0xAA);
    private static readonly SolidColorBrush SearchDimBrush = Freeze(0x88, 0x88, 0x88);
    private static readonly SolidColorBrush SearchHoverBrush = Freeze(0x2A, 0x2A, 0x2A);
    private static readonly SolidColorBrush ConfidenceHighBrush = Freeze(0x3D, 0xFF, 0x7A);
    private static readonly SolidColorBrush ConfidenceMidBrush = Freeze(0xE8, 0xA3, 0x17);
    private static readonly SolidColorBrush ConfidenceLowBrush = Freeze(0xFF, 0x77, 0x77);
    private static readonly SolidColorBrush QuestChipBrush = Freeze(0xE8, 0xA3, 0x17);
    private static readonly SolidColorBrush HideoutChipBrush = Freeze(0x9B, 0xE7, 0xFF);
    private static readonly SolidColorBrush ChipBgBrush = Freeze(0x22, 0x22, 0x22);

    private static readonly SolidColorBrush[] AmmoRatingBrushes =
    [
        Freeze(0x4A, 0x10, 0x10), // 0 dark red
        Freeze(0xC6, 0x28, 0x28), // 1 red
        Freeze(0xE6, 0x51, 0x00), // 2 orange
        Freeze(0x8D, 0x6E, 0x63), // 3 brown
        Freeze(0xC6, 0xA7, 0x00), // 4 gold
        Freeze(0x2E, 0x7D, 0x32), // 5 dark green
        Freeze(0x7C, 0xFC, 0x00), // 6 bright green
    ];

    private ItemScanResult? _shownResult;

    private static SolidColorBrush Freeze(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public event Action? Concealed;

    public ItemLensWindow()
    {
        InitializeComponent();
        NativeMethods.EnableWorkAreaMaximize(this);
        WindowPlacementHelper.Wire(this, App.Settings.ItemLensWindowPlacement, () => SettingsStore.Save(App.Settings));
        Loaded += OnWindowLoaded;
        Closed += (_, _) => App.Items.ItemsUpdated -= OnCatalogUpdated;
        App.Items.ItemsUpdated += OnCatalogUpdated;
    }

    public bool IsRevealed => _revealed && Opacity > 0.05;

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        ApplyLocalizedChrome();
        Loc.LanguageChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.LanguageChanged -= OnLanguageChanged;
        if (!_placementReady)
            ApplyPlacement();
    }

    private void OnLanguageChanged()
    {
        RunOnUi(() =>
        {
            ApplyLocalizedChrome();
            if (_shownResult != null && ResultPanel.Visibility == Visibility.Visible)
                BindUsageAndAmmo(_shownResult.Item);
        });
    }

    private void OnCatalogUpdated()
    {
        var shown = _shownResult;
        if (shown == null) return;
        RunOnUi(() =>
        {
            if (_shownResult == null || ResultPanel.Visibility != Visibility.Visible) return;
            var fresh = App.Items.FindById(_shownResult.Item.Id);
            if (fresh == null) return;
            _shownResult.Item = fresh;
            BindUsageAndAmmo(fresh);
        });
    }

    public void ApplyOpacity() =>
        _targetOpacity = Math.Clamp(App.Settings.ItemLensOpacity, 0.5, 1.0);

    public void ApplyPlacement()
    {
        var wa = SystemParameters.WorkArea;
        WindowPlacementHelper.Restore(this, App.Settings.ItemLensWindowPlacement, DefaultWidth, DefaultHeight, wa.Left + 16, wa.Top + 16);
        EnsureSize();
        WindowPlacementHelper.EnsureVisible(this);
        _placementReady = true;
    }

    /// <summary>Creates HWND while invisible — run once during app boot, not on F10.</summary>
    public void PrepareHidden()
    {
        ApplyOpacity();
        EnsureSize();
        if (!_placementReady)
            ApplyPlacement();

        _revealed = false;
        IsHitTestVisible = false;
        Opacity = 0;
        if (!IsVisible)
            Show();
    }

    /// <summary>Instant show — opacity only, no Show/Hide churn.</summary>
    public void RevealInstant()
    {
        ApplyOpacity();
        IsHitTestVisible = true;
        if (!IsVisible)
            Show();
        Opacity = _targetOpacity;
        _revealed = true;
    }

    private bool _suppressSearch;

    public void Conceal()
    {
        if (!_revealed && Opacity < 0.05) return;
        _revealed = false;
        IsHitTestVisible = false;
        Opacity = 0;
        Concealed?.Invoke();
    }

    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_forceClose) return;
        e.Cancel = true;
        Conceal();
    }

    public void ForceRedraw()
    {
        UpdateLayout();
        InvalidateVisual();
        NativeMethods.InvalidateWindow(this);
    }

    public void SetStatus(string text, bool loading = false) =>
        RunOnUi(() =>
        {
            StatusText.Text = text;
            StatusText.Foreground = loading ? Brushes.White : ReadyStatusBrush;
        });

    public void ShowScanFailed(string message)
    {
        RunOnUi(() =>
        {
            StatusText.Text = ShortHeaderStatus(message);
            StatusText.Foreground = ConfidenceLowBrush;
            StatusText.ToolTip = message;
            SetContentMode(ContentMode.Error);
            EmptyHintText.Text = message;
            ItemIcon.Source = null;
            _shownResult = null;
            AmmoPanel.Visibility = Visibility.Collapsed;
            UsageIcons.Children.Clear();
            EmptyHintText.Foreground = ConfidenceLowBrush;
        });
    }

    private static string ShortHeaderStatus(string message)
    {
        var first = message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var line = first.Length > 0 ? first[0].Trim() : message.Trim();
        var sep = line.IndexOf(" — ", StringComparison.Ordinal);
        if (sep < 0) sep = line.IndexOf(" - ", StringComparison.Ordinal);
        if (sep > 0) line = line[..sep].Trim();
        return line.Length <= 28 ? line : line[..27] + "…";
    }

    public void ShowEmpty(string? hint = null)
    {
        RunOnUi(() =>
        {
            SetContentMode(ContentMode.Empty);
            EmptyHintText.Foreground = EmptyHintBrush;
            EmptyHintText.Text = string.IsNullOrWhiteSpace(hint) ? Loc.T("ItemLens.Hint") : hint;
            StatusText.Text = Loc.T("ItemScan.Status.Ready");
            StatusText.Foreground = ReadyStatusBrush;
            StatusText.ToolTip = null;
            _shownResult = null;
            AmmoPanel.Visibility = Visibility.Collapsed;
            UsageIcons.Children.Clear();
        });
    }

    public void ShowOpening()
    {
        ItemIcon.Source = null;
        SetContentMode(ContentMode.Loading);
        ApplyLoadingChrome();
        LoadingTitleText.Text = Loc.T("ItemLens.Opening.Title");
        LoadingHintText.Text = Loc.T("ItemLens.Opening.Hint");
        StatusText.Text = Loc.T("ItemLens.Opening.Title");
        StatusText.Foreground = Brushes.White;
    }

    public void ShowLoading(string message, string? subtitle = null)
    {
        RunOnUi(() =>
        {
            ItemIcon.Source = null;
            ItemName.Text = "";
            ItemShort.Text = "";
            UsageIcons.Children.Clear();
            AmmoPanel.Visibility = Visibility.Collapsed;
            SetContentMode(ContentMode.Loading);
            ApplyLoadingChrome();
            LoadingTitleText.Text = Loc.T("ItemScan.Status.Identifying");
            LoadingHintText.Text = subtitle ?? message;
            StatusText.Text = Loc.T("ItemScan.Status.Scanning");
            StatusText.Foreground = Brushes.White;
        });
    }

    public async Task WaitMinLoadingVisibleAsync(TimeSpan minimum)
    {
        var wait = minimum - (DateTime.UtcNow - _loadingShownUtc);
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait).ConfigureAwait(false);
    }

    private void ApplyLoadingChrome()
    {
        _loadingShownUtc = DateTime.UtcNow;
        LoadingTitleText.Foreground = Brushes.White;
        LoadingHintText.Foreground = LoadingHintBrush;
        LoadingSpinner.Stroke = Brushes.White;
        StatusText.Foreground = Brushes.White;
        Dispatcher.BeginInvoke(RestartSpinnerAnimation, DispatcherPriority.Render);
    }

    private void RestartSpinnerAnimation()
    {
        _spinnerStoryboard?.Stop();
        LoadingSpinner.RenderTransform = new RotateTransform();
        var anim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(850))
        {
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(anim, LoadingSpinner);
        Storyboard.SetTargetProperty(anim, new PropertyPath("(UIElement.RenderTransform).(RotateTransform.Angle)"));
        _spinnerStoryboard = new Storyboard();
        _spinnerStoryboard.Children.Add(anim);
        _spinnerStoryboard.Begin();
    }

    public void ShowResult(ItemScanResult result)
    {
        RunOnUi(() =>
        {
            SetContentMode(ContentMode.Result);
            StatusText.Text = Loc.T("ItemScan.Status.Ready");
            StatusText.Foreground = ReadyStatusBrush;
            StatusText.ToolTip = null;

            _shownResult = result;
            var item = result.Item;
            ItemName.Text = ItemDisplayNames.Name(item);
            ItemShort.Text = ItemDisplayNames.ShortName(item);

            var w = result.SlotWidth > 0 ? result.SlotWidth : item.Width;
            var h = result.SlotHeight > 0 ? result.SlotHeight : item.Height;
            SizeBadgeText.Text = Loc.T("ItemLens.Size", w, h);
            ModeBadgeText.Text = result.Mode switch
            {
                "inspect" => Loc.T("ItemLens.Mode.Name"),
                "name" => Loc.T("ItemLens.Mode.Name"),
                "tooltip" => Loc.T("ItemLens.Mode.Name"),
                "shortname" or "shortname-highlight" => Loc.T("ItemLens.Mode.ShortName"),
                "search" => Loc.T("ItemLens.Mode.Search"),
                "ai" => Loc.T("ItemLens.Mode.Ai"),
                "icon-highlight" => Loc.T("ItemLens.Mode.Icon"),
                _ => Loc.T("ItemLens.Mode.Icon")
            };

            ApplyConfidenceBadge(result.Confidence, result.Mode);

            FleaPrice.Text = item.Avg24hPrice is > 0
                ? FormatRub(item.Avg24hPrice.Value)
                : "—";
            var trader = item.BestTraderRub;
            TraderPrice.Text = trader > 0 ? FormatRub(trader) : "—";
            var perSlot = trader > 0 ? trader / item.Slots :
                item.Avg24hPrice is > 0 ? item.Avg24hPrice.Value / item.Slots : 0;
            SlotPrice.Text = perSlot > 0 ? FormatRub(perSlot) : "—";

            BindUsageAndAmmo(item);

            var iconUrl = w * h > 1
                ? item.GridImageLink ?? item.IconLink
                : item.IconLink ?? item.GridImageLink;
            _ = LoadIconAsync(iconUrl);
        });
    }

    private void BindUsageAndAmmo(ItemDefinition item)
    {
        UsageIcons.Children.Clear();
        if (item.IsQuestItem)
            UsageIcons.Children.Add(UsageChip(Loc.T("ItemLens.Badge.Quest"), QuestChipBrush, FlagGeometry()));
        if (item.IsHideoutItem)
            UsageIcons.Children.Add(UsageChip(Loc.T("ItemLens.Badge.Hideout"), HideoutChipBrush, HouseGeometry()));

        var ammo = App.Items.ResolveAmmo(item);
        if (ammo == null)
        {
            AmmoPanel.Visibility = Visibility.Collapsed;
            return;
        }

        AmmoPanel.Visibility = Visibility.Visible;
        AmmoPenText.Text = Loc.T("ItemLens.Ammo.Pen", ammo.PenetrationPower);
        AmmoDmgText.Text = Loc.T("ItemLens.Ammo.Dmg", ammo.Damage);
        AmmoCaliberText.Text = string.IsNullOrEmpty(ammo.Caliber) ? "" : ammo.Caliber;

        AmmoClassHeaders.Children.Clear();
        AmmoRatingRow.Children.Clear();
        for (var c = 1; c <= 6; c++)
        {
            AmmoClassHeaders.Children.Add(new TextBlock
            {
                Text = c.ToString(),
                FontSize = 9,
                Foreground = ReadyStatusBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            var rating = AmmoBallistics.ClassRating(ammo.PenetrationPower, c);
            var cell = new Border
            {
                Background = AmmoRatingBrushes[rating],
                Margin = new Thickness(1, 0, 1, 0),
                Padding = new Thickness(0, 4, 0, 4),
                ToolTip = Loc.T($"ItemLens.Ammo.Rating.{rating}", c)
            };
            cell.Child = new TextBlock
            {
                Text = rating.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = rating >= 5 ? Brushes.Black : Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            AmmoRatingRow.Children.Add(cell);
        }

        var best = AmmoBallistics.BestEffectiveClass(ammo.PenetrationPower);
        AmmoVerdict.Text = best >= 4
            ? Loc.T("ItemLens.Ammo.Verdict.Good", best)
            : Loc.T("ItemLens.Ammo.Verdict.Weak");
        AmmoVerdict.Foreground = best >= 5 ? ConfidenceHighBrush
            : best >= 4 ? ConfidenceMidBrush
            : ConfidenceLowBrush;
    }

    private static Border UsageChip(string label, SolidColorBrush accent, Geometry icon)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = icon,
            Fill = accent,
            Width = 11,
            Height = 11,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        var text = new TextBlock
        {
            Text = label,
            Foreground = accent,
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(path);
        row.Children.Add(text);
        return new Border
        {
            Background = ChipBgBrush,
            BorderBrush = accent,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 2),
            Child = row,
            ToolTip = label
        };
    }

    private static Geometry FlagGeometry() =>
        Geometry.Parse("M2,1 V17 M2,1 H12 L10,5 12,9 H2");

    private static Geometry HouseGeometry() =>
        Geometry.Parse("M1,9 L9,2 L17,9 V16 H11 V12 H7 V16 H1 Z");

    private static void RunOnUi(Action action)
    {
        var d = Application.Current?.Dispatcher;
        if (d == null || d.CheckAccess())
            action();
        else
            d.BeginInvoke(action);
    }

    private void ApplyLocalizedChrome()
    {
        FooterHint.Text = Loc.T("ItemLens.Footer");
        SearchPlaceholder.Text = Loc.T("ItemLens.Search.Placeholder");
        if (LoadingPanel.Visibility != Visibility.Visible)
        {
            StatusText.Text = Loc.T("ItemScan.Status.Ready");
            StatusText.Foreground = ReadyStatusBrush;
        }
        if (EmptyPanel.Visibility == Visibility.Visible && LoadingPanel.Visibility != Visibility.Visible)
            EmptyHintText.Text = Loc.T("ItemLens.Hint");
    }

    private void ApplyConfidenceBadge(double confidence, string mode)
    {
        if (mode is "name" or "shortname" or "shortname-highlight" or "tooltip" or "inspect" or "search" or "ai")
        {
            ConfidenceBadgeText.Text = mode switch
            {
                "shortname" or "shortname-highlight" => Loc.T("ItemLens.Confidence.ShortName"),
                "search" => Loc.T("ItemLens.Confidence.Search"),
                "ai" => Loc.T("ItemLens.Confidence.Ai"),
                _ => Loc.T("ItemLens.Confidence.Name")
            };
            ConfidenceBadgeText.Foreground = ConfidenceHighBrush;
            return;
        }

        var pct = (int)Math.Round(confidence * 100);
        ConfidenceBadgeText.Text = Loc.T("ItemLens.Confidence.Pct", pct);
        ConfidenceBadgeText.Foreground = confidence switch
        {
            >= 0.96 => ConfidenceHighBrush,
            >= 0.90 => ConfidenceMidBrush,
            _ => ConfidenceLowBrush
        };
    }

    private enum ContentMode { Empty, Loading, Result, Error }

    private void SetContentMode(ContentMode mode)
    {
        EmptyPanel.Visibility = mode is ContentMode.Empty or ContentMode.Error
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoadingPanel.Visibility = mode == ContentMode.Loading ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility = mode == ContentMode.Result ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EnsureSize()
    {
        if (double.IsNaN(Width) || Width < MinWidth)
            Width = DefaultWidth;
        if (double.IsNaN(Height) || Height < MinHeight)
            Height = DefaultHeight;
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
                ForceRedraw();
            });
        }
        catch { /* ignore */ }
    }

    private static string FormatRub(long v) =>
        v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " ₽";

    private void Header_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is TextBox or ListBox or ListBoxItem or ScrollViewer)
            return;
        if (e.ChangedButton == MouseButton.Left)
        {
            try { DragMove(); }
            catch { /* ignore */ }
        }
    }

    private void SearchArea_MouseDown(object sender, MouseButtonEventArgs e)
    {
        ItemSearchBox.Focus();
        e.Handled = true;
    }

    private void ItemSearch_Changed(object sender, TextChangedEventArgs e)
    {
        var q = ItemSearchBox.Text ?? "";
        SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(q)
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_suppressSearch) return;
        RefreshSearchResults(q);
    }

    private void ItemSearch_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ItemSearchBox.Clear();
            HideSearchResults();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Enter or Key.Down)
        {
            var first = SearchResultsList.Children.OfType<FrameworkElement>().FirstOrDefault();
            if (first?.Tag is ItemDefinition item)
            {
                ShowSearchItem(item);
                e.Handled = true;
            }
        }
    }

    private void RefreshSearchResults(string query)
    {
        SearchResultsList.Children.Clear();
        if (query.Trim().Length < 2 || App.Items.Items.Count == 0)
        {
            HideSearchResults();
            return;
        }

        var hits = App.Items.Search(query, 10);
        if (hits.Count == 0)
        {
            SearchResultsList.Children.Add(new TextBlock
            {
                Text = Loc.T("ItemLens.Search.Empty"),
                Foreground = SearchDimBrush,
                FontSize = 10,
                Margin = new Thickness(8, 6, 8, 6),
                TextWrapping = TextWrapping.Wrap
            });
            SearchResultsPanel.Visibility = Visibility.Visible;
            return;
        }

        foreach (var item in hits)
        {
            var row = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(8, 5, 8, 5),
                Cursor = Cursors.Hand,
                Tag = item
            };
            var col = new StackPanel();
            col.Children.Add(new TextBlock
            {
                Text = ItemDisplayNames.Name(item),
                Foreground = Brushes.White,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            col.Children.Add(new TextBlock
            {
                Text = ItemDisplayNames.ShortName(item),
                Foreground = SearchDimBrush,
                FontSize = 9,
                Margin = new Thickness(0, 1, 0, 0)
            });
            var ammo = App.Items.ResolveAmmo(item);
            if (ammo != null)
                col.Children.Add(BuildSearchAmmoStrip(ammo));
            row.Child = col;
            row.MouseLeftButtonDown += (_, ev) =>
            {
                ShowSearchItem(item);
                ev.Handled = true;
            };
            row.MouseEnter += (_, _) =>
                row.Background = SearchHoverBrush;
            row.MouseLeave += (_, _) =>
                row.Background = Brushes.Transparent;
            SearchResultsList.Children.Add(row);
        }

        SearchResultsPanel.Visibility = Visibility.Visible;
    }

    private static UniformGrid BuildSearchAmmoStrip(ItemAmmoStats ammo)
    {
        var grid = new UniformGrid
        {
            Columns = 6,
            Margin = new Thickness(0, 4, 0, 0)
        };
        for (var c = 1; c <= 6; c++)
        {
            var rating = AmmoBallistics.ClassRating(ammo.PenetrationPower, c);
            grid.Children.Add(new Border
            {
                Background = AmmoRatingBrushes[rating],
                Margin = new Thickness(1, 0, 1, 0),
                Height = 7,
                ToolTip = Loc.T($"ItemLens.Ammo.Rating.{rating}", c)
            });
        }
        return grid;
    }

    private void HideSearchResults()
    {
        SearchResultsList.Children.Clear();
        SearchResultsPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowSearchItem(ItemDefinition item)
    {
        _suppressSearch = true;
        ItemSearchBox.Text = ItemDisplayNames.Name(item);
        _suppressSearch = false;
        SearchPlaceholder.Visibility = Visibility.Collapsed;
        HideSearchResults();
        ShowResult(new ItemScanResult
        {
            Item = item,
            Confidence = 1,
            Mode = "search",
            SlotWidth = item.Width,
            SlotHeight = item.Height
        });
    }
}
