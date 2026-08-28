using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class MainWindow : Window
{
    private static readonly TimeSpan MinLoadingTime = TimeSpan.FromSeconds(2);

    private OverlayWindow? _overlay;
    private ItemLensWindow? _itemLens;
    private ItemScanClickWatcher? _itemScanClickWatcher;
    private HwndSource? _hotkeySource;
    private bool _suppress;

    public MainWindow()
    {
        InitializeComponent();
        SetLoading(true);
        NativeMethods.EnableWorkAreaMaximize(this);
        WindowPlacementHelper.Wire(this, App.Settings.MainWindowPlacement, () => SettingsStore.Save(App.Settings));
        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Closed += (_, _) =>
        {
            UnregisterHotkeys();
            StopItemScanWatcher();
            SettingsWindow.SettingsApplied -= OnSettingsApplied;
            Loc.LanguageChanged -= OnLanguageChanged;
            _overlay?.Close();
            _itemLens?.Close();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => _ = OnLoadedAsync();

    private async Task OnLoadedAsync()
    {
        SetLoading(true);

        WindowPlacementHelper.Restore(this, App.Settings.MainWindowPlacement, Width, Height);
        WindowPlacementHelper.EnsureVisible(this);

        try
        {
            await App.EnsureInitializedAsync();
        }
        catch (Exception ex)
        {
            SetLoading(false);
            MessageBox.Show(ex.Message, "Tarkovy", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        _suppress = true;
        AppVersionLabel.Text = ProductInfo.AppVersionLabel;
        RefreshEftTargetLabel();
        PopulateMapCombo();
        SelectMapId(App.Settings.SelectedMapId);
        ShowExtractsBox.IsChecked = App.Settings.ShowExtracts;
        ShowMinesBox.IsChecked = App.Settings.ShowMines;
        ShowSpawnsBox.IsChecked = App.Settings.ShowSpawns;
        ShowLabelsBox.IsChecked = App.Settings.ShowMarkerLabels;
        ShowQuestsBox.IsChecked = App.Settings.ShowQuests;
        _suppress = false;

        WireRuntime();
        PreviewMap.LayerToggled += OnMapLayerToggled;
        PreviewMap.WaypointChanged += wp =>
        {
            App.Settings.ActiveWaypoint = wp;
            _overlay?.SetWaypoint(wp);
        };
        SettingsWindow.SettingsApplied += OnSettingsApplied;
        Loc.LanguageChanged += OnLanguageChanged;
        RegisterHotkeys();
        WireItemScanEvents();
        RefreshHud();

        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
        UpdateLoadingSnapshot();
        await Task.Delay(MinLoadingTime);

        PreviewMap.Visibility = Visibility.Visible;
        SetLoading(false);
        PushMapToViews();
        Activate();
        _ = WarmupAndPushMapAsync();
        _ = DeferOptionalServicesAsync();
    }

    private void SetLoading(bool loading)
    {
        if (loading)
        {
            PreviewMap.Visibility = Visibility.Collapsed;
            AppChrome.IsEnabled = false;
            LoadingOverlay.Opacity = 1;
            LoadingOverlay.Visibility = Visibility.Visible;
            UpdateLoadingSnapshot();
        }
        else
        {
            PreviewMap.Visibility = Visibility.Visible;
            AppChrome.IsEnabled = true;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            LoadingOverlay.Opacity = 1;
            LoadingSnapshot.Source = null;
        }
    }

    private async Task WarmupAndPushMapAsync()
    {
        try
        {
            await PreviewMap.WarmupAsync().WaitAsync(TimeSpan.FromSeconds(45));
            await Dispatcher.InvokeAsync(PushMapToViews);
        }
        catch
        {
            /* WebView failed — error page stays in map control */
        }
    }

    private static readonly TimeSpan DeferredServicesDelay = TimeSpan.FromSeconds(4);

    private async Task DeferOptionalServicesAsync()
    {
        await Task.Delay(DeferredServicesDelay);
        await Dispatcher.InvokeAsync(() =>
        {
            StartItemScanServices();
            if (App.Settings.OverlayVisible)
                ShowOverlay();
            if (App.Settings.ItemLensVisible)
                ShowItemLens();
        });
    }

    private void UpdateLoadingSnapshot()
    {
        try
        {
            AppChrome.UpdateLayout();
            var w = (int)Math.Ceiling(AppChrome.ActualWidth);
            var h = (int)Math.Ceiling(AppChrome.ActualHeight);
            if (w < 1 || h < 1) return;

            var shot = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            shot.Render(AppChrome);
            LoadingSnapshot.Source = shot;
        }
        catch
        {
            LoadingSnapshot.Source = null;
        }
    }

    private void PopulateMapCombo()
    {
        var selected = App.Settings.SelectedMapId;
        MapCombo.Items.Clear();
        foreach (var map in App.Maps.Maps)
        {
            MapCombo.Items.Add(new ComboBoxItem
            {
                Content = map.Name,
                Tag = map.Id,
                Foreground = (Brush)FindResource("BrushText"),
                Background = (Brush)FindResource("BrushBgRaised")
            });
        }

        SelectMapId(selected);
    }

    private void RefreshEftTargetLabel()
    {
        EftTargetLabel.Text = Loc.T("Main.EftTarget", ProductInfo.EftPatch, ProductInfo.EftSeason);
        EftTargetLabel.ToolTip = Loc.T("Main.Tooltip.EftTarget", ProductInfo.EftBuild, ProductInfo.EftSeason);
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            var selected = App.Settings.SelectedMapId;
            _suppress = true;
            PopulateMapCombo();
            SelectMapId(selected);
            _suppress = false;
            RefreshEftTargetLabel();
            PreviewMap.ApplyLanguage();
            _overlay?.ApplyLanguage();
            var map = App.Maps.FindById(App.Settings.SelectedMapId) ?? App.Maps.Maps.FirstOrDefault();
            if (map != null)
            {
                PreviewMap.RefreshMapPayload(map);
                _overlay?.RefreshMapPayload(map);
            }
            UpdateMaximizeGlyph();
            RebuildQuestList();
            RefreshHud();
        });
    }

    private void WireRuntime()
    {
        App.Logs.MapDetected += map => Dispatcher.Invoke(() =>
        {
            App.Raid.SetMap(map);
            App.Settings.SelectedMapId = map.Id;
            App.Settings.ActiveWaypoint = null;
            SelectMapId(map.Id);
            PushMapToViews();
            RefreshHud();
            if (_overlay is { IsVisible: true })
                _overlay.ApplyMiniLayout();
        });
        App.Logs.RaidStarted += () => Dispatcher.Invoke(() =>
        {
            App.Shots.ResetRaidCounters();
            App.Raid.SetRaidStarted();
            RefreshHud();
        });
        App.Logs.RaidEnded += () => Dispatcher.Invoke(() => EndRaidInternal(fromLogs: true));
        App.Logs.Status += msg => Dispatcher.Invoke(() => AppendFooter(msg));
        App.Shots.PositionUpdated += fix => Dispatcher.Invoke(() =>
        {
            App.Raid.SetPosition(fix);
            PreviewMap.SetPlayer(fix);
            _overlay?.SetPlayer(fix);
            RefreshHud();
        });
        App.Shots.Status += msg => Dispatcher.Invoke(() => AppendFooter(msg));
        App.Shots.DeletedCount += _ => Dispatcher.Invoke(RefreshHud);
        App.Raid.Changed += () => Dispatcher.Invoke(RefreshHud);
        App.Maps.MarkersUpdated += () => Dispatcher.Invoke(PushMapToViews);
    }

    private void SelectMapId(string id)
    {
        for (var i = 0; i < MapCombo.Items.Count; i++)
        {
            if (MapCombo.Items[i] is ComboBoxItem item && string.Equals(item.Tag as string, id, StringComparison.OrdinalIgnoreCase))
            {
                MapCombo.SelectedIndex = i;
                return;
            }
        }

        if (MapCombo.Items.Count > 0)
            MapCombo.SelectedIndex = 0;
    }

    private void PushMapToViews()
    {
        var map = App.Maps.FindById(App.Settings.SelectedMapId) ?? App.Maps.Maps.FirstOrDefault();
        if (map == null) return;
        App.Raid.SetMap(map);
        var extracts = App.Maps.ExtractsFor(map.Id);
        var mines = App.Maps.MinesFor(map.Id);
        var spawns = App.Maps.SpawnsFor(map.Id);
        var quests = App.Maps.QuestsFor(map.Id);
        var labels = App.Settings.ShowMarkerLabels;
        PreviewMap.LoadMap(map, extracts, mines, spawns, labels);
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        PreviewMap.SetQuests(quests, QuestState.TrackingSlugs());
        PreviewMap.SetWaypoint(App.Settings.ActiveWaypoint);
        PreviewMap.SetFollow(App.Settings.FollowPlayer);
        PreviewMap.SetAutoFloor(App.Settings.AutoFloorFromHeight);
        if (_overlay != null)
        {
            _overlay.QuestSelectionChanged -= OnOverlayQuestSelectionChanged;
            _overlay.WaypointRequested -= OnOverlayWaypointRequested;
            _overlay.LayersChanged -= OnOverlayLayersChanged;
            _overlay.LoadMap(map, extracts, mines, spawns, labels, quests);
            _overlay.SetFollow(App.Settings.FollowPlayer);
            _overlay.SetGlassOpacity(App.Settings.OverlayOpacity);
            _overlay.QuestSelectionChanged += OnOverlayQuestSelectionChanged;
            _overlay.WaypointRequested += OnOverlayWaypointRequested;
            _overlay.LayersChanged += OnOverlayLayersChanged;
        }
        MapTitle.Text = map.Name.ToUpperInvariant();
        ScheduleRebuildQuestList();
    }

    private void ScheduleRebuildQuestList()
    {
        Dispatcher.BeginInvoke(RebuildQuestList, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void PushMarkersToViews()
    {
        var map = App.Maps.FindById(App.Settings.SelectedMapId) ?? App.Maps.Maps.FirstOrDefault();
        if (map == null) return;
        var extracts = App.Maps.ExtractsFor(map.Id);
        var mines = App.Maps.MinesFor(map.Id);
        var spawns = App.Maps.SpawnsFor(map.Id);
        var quests = App.Maps.QuestsFor(map.Id);
        var labels = App.Settings.ShowMarkerLabels;
        PreviewMap.SetMarkers(extracts, mines, spawns, labels);
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        PreviewMap.SetQuests(quests, QuestState.TrackingSlugs());
        _overlay?.SetMarkers(extracts, mines, spawns, labels);
        _overlay?.SetQuests(quests);
    }

    private void RebuildQuestList()
    {
        QuestListPanel.Children.Clear();
        var map = App.Maps.FindById(App.Settings.SelectedMapId) ?? App.Maps.Maps.FirstOrDefault();
        if (map == null) return;

        var quests = App.Maps.QuestsFor(map.Id);
        if (quests.Count == 0)
        {
            QuestListPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("Overlay.Quests.Empty"),
                FontSize = 11,
                Foreground = (Brush)FindResource("BrushTextDim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        var filter = QuestSearchBox.Text.Trim();
        var tooltip = Loc.T("Main.Quests.MapTooltip", map.Name);
        foreach (var q in quests.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!QuestListUi.MatchesFilter(q, filter))
                continue;
            QuestListPanel.Children.Add(QuestListUi.BuildRow(q, OnQuestStateChanged, tooltip));
        }

        if (QuestListPanel.Children.Count == 0 && quests.Count > 0 && filter.Length > 0)
        {
            QuestListPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("Main.Quests.Search.Empty"),
                FontSize = 11,
                Foreground = (Brush)FindResource("BrushTextDim"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
        }
    }

    private void QuestSearch_Changed(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        RebuildQuestList();
    }

    private void OnQuestStateChanged()
    {
        if (_suppress) return;
        PushMarkersToViews();
        PreviewMap.SetWaypoint(App.Settings.ActiveWaypoint);
        RebuildQuestList();
        _overlay?.RefreshQuestList();
        _overlay?.SetWaypoint(App.Settings.ActiveWaypoint);
    }

    private void OnOverlayQuestSelectionChanged()
    {
        RebuildQuestList();
        var map = App.Maps.FindById(App.Settings.SelectedMapId) ?? App.Maps.Maps.FirstOrDefault();
        if (map == null) return;
        PreviewMap.SetQuests(App.Maps.QuestsFor(map.Id), QuestState.TrackingSlugs());
    }

    private void OnOverlayWaypointRequested(MapWaypoint? wp)
    {
        App.Settings.ActiveWaypoint = wp;
        PreviewMap.SetWaypoint(wp);
    }

    private void OnOverlayLayersChanged()
    {
        _suppress = true;
        ShowExtractsBox.IsChecked = App.Settings.ShowExtracts;
        ShowMinesBox.IsChecked = App.Settings.ShowMines;
        ShowSpawnsBox.IsChecked = App.Settings.ShowSpawns;
        ShowLabelsBox.IsChecked = App.Settings.ShowMarkerLabels;
        ShowQuestsBox.IsChecked = App.Settings.ShowQuests;
        _suppress = false;
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
    }

    private void OnMapLayerToggled(string key, bool value)
    {
        switch (key)
        {
            case "extracts":
                App.Settings.ShowExtracts = value;
                _suppress = true;
                ShowExtractsBox.IsChecked = value;
                _suppress = false;
                break;
            case "mines":
                App.Settings.ShowMines = value;
                _suppress = true;
                ShowMinesBox.IsChecked = value;
                _suppress = false;
                break;
            case "spawns":
                App.Settings.ShowSpawns = value;
                _suppress = true;
                ShowSpawnsBox.IsChecked = value;
                _suppress = false;
                break;
            case "quests":
                App.Settings.ShowQuests = value;
                _suppress = true;
                ShowQuestsBox.IsChecked = value;
                _suppress = false;
                break;
            case "labels":
                App.Settings.ShowMarkerLabels = value;
                _suppress = true;
                ShowLabelsBox.IsChecked = value;
                _suppress = false;
                break;
            default:
                return;
        }
        SettingsStore.Save(App.Settings);
        _overlay?.SetMarkers(
            App.Maps.ExtractsFor(App.Settings.SelectedMapId),
            App.Maps.MinesFor(App.Settings.SelectedMapId),
            App.Maps.SpawnsFor(App.Settings.SelectedMapId),
            App.Settings.ShowMarkerLabels);
    }

    private void RefreshHud()
    {
        var pos = App.Raid.LastPosition;
        var mapName = App.Raid.CurrentMap?.Name ?? "—";
        var posTxt = pos == null
            ? Loc.T("Footer.NoFix")
            : $"X {pos.X:0.0}  Y {pos.Y:0.0}  Z {pos.Z:0.0}  YAW {pos.Yaw:0}";
        Footer.Text = Loc.T("Footer.Hud", App.Raid.StatusLabel, mapName, posTxt, App.Shots.DeletedThisRaid);
    }

    private void AppendFooter(string msg)
    {
        Footer.Text = msg;
    }

    private void Markers_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || !IsLoaded) return;
        App.Settings.ShowExtracts = ShowExtractsBox.IsChecked == true;
        App.Settings.ShowMines = ShowMinesBox.IsChecked == true;
        App.Settings.ShowSpawns = ShowSpawnsBox.IsChecked == true;
        App.Settings.ShowMarkerLabels = ShowLabelsBox.IsChecked == true;
        App.Settings.ShowQuests = ShowQuestsBox.IsChecked == true;
        SettingsStore.Save(App.Settings);
        PushMarkersToViews();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow { Owner = this };
        win.ShowDialog();
    }

    private void OnSettingsApplied()
    {
        _suppress = true;
        ShowExtractsBox.IsChecked = App.Settings.ShowExtracts;
        ShowMinesBox.IsChecked = App.Settings.ShowMines;
        ShowSpawnsBox.IsChecked = App.Settings.ShowSpawns;
        ShowLabelsBox.IsChecked = App.Settings.ShowMarkerLabels;
        ShowQuestsBox.IsChecked = App.Settings.ShowQuests;
        _suppress = false;
        _itemLens?.ApplyOpacity();
        if (App.Settings.ItemScanEnabled)
            StartItemScanWatcher();
        else
            StopItemScanWatcher();
        PushMapToViews();
        RefreshHud();
    }

    private void MapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (MapCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
        App.Settings.SelectedMapId = id;
        App.Settings.ActiveWaypoint = null;
        PushMapToViews();
        SettingsStore.Save(App.Settings);
    }

    private void ShowOverlay_Click(object sender, RoutedEventArgs e) => ShowOverlay();

    private void ToggleOverlayPanel_Click(object sender, RoutedEventArgs e) => _overlay?.ToggleExpanded();

    private void ItemLens_Click(object sender, RoutedEventArgs e) => ToggleItemLens();

    private void EndRaid_Click(object sender, RoutedEventArgs e) => EndRaidInternal(fromLogs: false);

    private void EndRaidInternal(bool fromLogs)
    {
        var cleanup = !fromLogs || App.Settings.AutoCleanupOnRaidEnd;
        if (cleanup)
            App.Shots.SweepLeftovers();
        App.Raid.SetRaidEnded();
        PreviewMap.SetPlayer(null);
        _overlay?.SetPlayer(null);
        RefreshHud();
        if (fromLogs && cleanup)
            AppendFooter(Loc.T("Footer.RaidEndedCleaned"));
        else if (fromLogs)
            AppendFooter(Loc.T("Footer.RaidEndedNoAuto"));
    }

    private void ShowOverlay()
    {
        _overlay ??= new OverlayWindow();
        _overlay.SetGlassOpacity(App.Settings.OverlayOpacity);
        PushMapToViews();
        if (_overlay.WindowState == WindowState.Minimized)
            _overlay.WindowState = WindowState.Normal;
        _overlay.Show();
        _overlay.RestorePlacement();
        App.Settings.OverlayVisible = true;
        SettingsStore.Save(App.Settings);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
            return;
        }

        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            DragMove();
    }

    private void UpdateMaximizeGlyph()
    {
        if (MaximizeBtn == null) return;
        MaximizeBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        MaximizeBtn.ToolTip = WindowState == WindowState.Maximized
            ? Loc.T("Main.Tooltip.Restore")
            : Loc.T("Main.Tooltip.Maximize");
    }

    private void RegisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        _hotkeySource = HwndSource.FromHwnd(hwnd);
        _hotkeySource?.AddHook(WndProc);
        NativeMethods.RegisterHotKey(hwnd, 8, NativeMethods.ModNorepeat, NativeMethods.VkF8);
        NativeMethods.RegisterHotKey(hwnd, 9, NativeMethods.ModNorepeat, NativeMethods.VkF9);
        NativeMethods.RegisterHotKey(hwnd, 10, NativeMethods.ModNorepeat, NativeMethods.VkF10);
    }

    private void UnregisterHotkeys()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(hwnd, 8);
                NativeMethods.UnregisterHotKey(hwnd, 9);
                NativeMethods.UnregisterHotKey(hwnd, 10);
            }
        }
        catch
        {
            // ignore
        }

        _hotkeySource?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmGetMinMaxInfo)
        {
            if (NativeMethods.TryHandleGetMinMaxInfo(hwnd, lParam, this))
                handled = true;
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WmHotkey)
        {
            var id = wParam.ToInt32();
            if (id == 8)
            {
                ToggleOverlayVisible();
                handled = true;
            }
            else if (id == 9)
            {
                _overlay?.ToggleExpanded();
                handled = true;
            }
            else if (id == 10)
            {
                ToggleItemLens();
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ToggleOverlayVisible()
    {
        if (_overlay == null)
        {
            ShowOverlay();
            return;
        }

        if (_overlay.IsVisible)
        {
            _overlay.Hide();
            App.Settings.OverlayVisible = false;
        }
        else
        {
            PushMapToViews();
            if (_overlay.WindowState == WindowState.Minimized)
                _overlay.WindowState = WindowState.Normal;
            _overlay.Show();
            _overlay.RestorePlacement();
            App.Settings.OverlayVisible = true;
        }

        SettingsStore.Save(App.Settings);
    }

    private void WireItemScanEvents()
    {
        _itemScanClickWatcher = new ItemScanClickWatcher(Dispatcher);
        _itemScanClickWatcher.ClickDetected += (x, y, shift) =>
        {
            if (!App.Settings.ItemScanEnabled) return;
            if (shift)
                App.ItemScan.ScanIconAt(x, y);
            else
                App.ItemScan.ScanNameAt(x, y);
        };

        App.ItemScan.ScanCompleted += r =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _itemLens ??= CreateItemLens();
                _itemLens.ShowResult(r);
                if (!_itemLens.IsVisible)
                {
                    _itemLens.Show();
                    App.Settings.ItemLensVisible = true;
                }
            });
        };
        App.ItemScan.ScanFailed += msg =>
        {
            Dispatcher.BeginInvoke(() => _itemLens?.SetStatus(msg));
        };
        App.ItemScan.StatusChanged += msg =>
        {
            Dispatcher.BeginInvoke(() => _itemLens?.SetStatus(msg));
        };
    }

    private void StartItemScanServices()
    {
        if (App.Settings.ItemScanEnabled)
            StartItemScanWatcher();
        _ = App.ItemScan.EnsureReadyAsync();
    }

    private ItemLensWindow CreateItemLens()
    {
        var win = new ItemLensWindow();
        win.Closed += (_, _) =>
        {
            App.Settings.ItemLensVisible = false;
            _itemLens = null;
            SettingsStore.Save(App.Settings);
        };
        return win;
    }

    private void ShowItemLens()
    {
        _itemLens ??= CreateItemLens();
        _itemLens.ApplyOpacity();
        _itemLens.ShowEmpty();
        _itemLens.Show();
        App.Settings.ItemLensVisible = true;
        SettingsStore.Save(App.Settings);
        if (!App.Settings.ItemLensWindowPlacement.IsValid)
            PlaceItemLensDefault();
    }

    private void PlaceItemLensDefault()
    {
        if (_itemLens == null) return;
        var wa = SystemParameters.WorkArea;
        _itemLens.Left = wa.Left + 16;
        _itemLens.Top = wa.Top + 16;
    }

    private void ToggleItemLens()
    {
        if (_itemLens == null || !_itemLens.IsVisible)
            ShowItemLens();
        else
        {
            _itemLens.Hide();
            App.Settings.ItemLensVisible = false;
            SettingsStore.Save(App.Settings);
        }
    }

    private void StartItemScanWatcher()
    {
        if (!App.Settings.ItemScanEnabled) return;
        _itemScanClickWatcher ??= new ItemScanClickWatcher(Dispatcher);
        _itemScanClickWatcher.Start();
    }

    private void StopItemScanWatcher()
    {
        _itemScanClickWatcher?.Stop();
    }
}
