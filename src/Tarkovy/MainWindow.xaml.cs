using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class MainWindow : Window
{
    private static readonly TimeSpan MinLoadingTime = TimeSpan.FromSeconds(3);

    private OverlayWindow? _overlay;
    private ItemLensWindow? _itemLens;
    private bool _itemLensReady;
    private ItemScanClickWatcher? _itemScanClickWatcher;
    private HwndSource? _hotkeySource;
    private bool _suppress;
    private bool _bootStarted;
    private LoadingTerminal? _loadingTerminal;
    private int _itemScanGeneration;
    private int _itemLensOpenGeneration;
    private DateTime _itemScanStartedUtc;
    private bool _shiftScanPending;
    private static readonly TimeSpan MinItemScanLoading = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan MinItemLensOpenLoading = TimeSpan.FromMilliseconds(1200);
    private bool _itemLensOpening;
    private bool _itemLensAcceptsScans;
    private string _listMode = "quests";
    private bool _poiBusy;

    public MainWindow()
    {
        InitializeComponent();
        _loadingTerminal = new LoadingTerminal(LoadingTerminalText);
        ShowLoadingOverlay();
        NativeMethods.EnableWorkAreaMaximize(this);
        WindowPlacementHelper.Wire(this, App.Settings.MainWindowPlacement, () => SettingsStore.Save(App.Settings));
        ContentRendered += OnContentRendered;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Closed += (_, _) =>
        {
            UnregisterHotkeys();
            StopItemScanWatcher();
            SettingsWindow.SettingsApplied -= OnSettingsApplied;
            Loc.LanguageChanged -= OnLanguageChanged;
            _overlay?.Close();
            _itemLens?.ForceClose();
            _itemLens = null;
            App.ShutdownItemScan();
        };
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        if (_bootStarted) return;
        _bootStarted = true;
        Dispatcher.BeginInvoke(WarmItemLensWindow, System.Windows.Threading.DispatcherPriority.Background);
        _ = BootAsyncSafe();
    }

    private async Task BootAsyncSafe()
    {
        try
        {
            await OnLoadedAsync();
        }
        catch (Exception ex)
        {
            HideLoadingOverlay();
            MessageBox.Show(ex.Message, "Tarkovy", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private async Task OnLoadedAsync()
    {
        var bootStart = DateTime.UtcNow;
        _loadingTerminal ??= new LoadingTerminal(LoadingTerminalText);
        _loadingTerminal.Seed();

        WindowPlacementHelper.Restore(this, App.Settings.MainWindowPlacement, Width, Height);
        WindowPlacementHelper.EnsureVisible(this);

        try
        {
            var initTask = App.EnsureInitializedAsync();
            await _loadingTerminal.TypeLineAsync("Loading.Term.Api");
            await _loadingTerminal.TypeLineAsync("Loading.Term.Assets");
            await _loadingTerminal.TypeLineAsync("Loading.Term.Items");
            await _loadingTerminal.TypeLineAsync("Loading.Term.Quests");
            await initTask;
        }
        catch (Exception ex)
        {
            HideLoadingOverlay();
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
        SyncPoiMasters();
        SyncPanelTabs();
        _suppress = false;

        WireRuntime();
        PreviewMap.LayerToggled += OnMapLayerToggled;
        PreviewMap.PoiCategoryToggled += OnPoiCategoryToggled;
        PreviewMap.WaypointChanged += wp =>
        {
            App.Settings.ActiveWaypoint = wp;
            _overlay?.SetWaypoint(wp);
        };
        SettingsWindow.SettingsApplied += OnSettingsApplied;
        SettingsHost.Closed += HideSettingsDrawer;
        Loc.LanguageChanged += OnLanguageChanged;
        RegisterHotkeys();
        WireItemScanEvents();
        RefreshHud();

        await _loadingTerminal.TypeLineAsync("Loading.Term.Markers");
        await _loadingTerminal.TypeLineAsync("Loading.Term.ItemLens");
        WarmItemLensWindow();
        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
        UpdateLoadingSnapshot();
        await _loadingTerminal.TypeLineAsync("Loading.Term.Ui");
        await _loadingTerminal.TypeLineAsync("Loading.Term.Ready");
        _loadingTerminal.Complete();

        var bootElapsed = DateTime.UtcNow - bootStart;
        if (bootElapsed < MinLoadingTime)
            await Task.Delay(MinLoadingTime - bootElapsed);

        PreviewMap.Visibility = Visibility.Visible;
        HideLoadingOverlay();
        PushMapToViews();
        Activate();
        _ = WarmupAndPushMapAsync();
        _ = DeferOptionalServicesAsync();
    }

    private void ShowLoadingOverlay()
    {
        PreviewMap.Visibility = Visibility.Collapsed;
        AppChrome.IsEnabled = false;
        LoadingOverlay.Opacity = 1;
        LoadingOverlay.Visibility = Visibility.Visible;
        LoadingTerminalPanel.Visibility = Visibility.Visible;
        _loadingTerminal?.Seed();
    }

    private void HideLoadingOverlay()
    {
        PreviewMap.Visibility = Visibility.Visible;
        AppChrome.IsEnabled = true;
        LoadingOverlay.Visibility = Visibility.Collapsed;
        LoadingOverlay.Opacity = 1;
        LoadingSnapshot.Source = null;
        LoadingTerminalText.Text = "";
    }

    private void SetLoading(bool loading)
    {
        if (loading)
            ShowLoadingOverlay();
        else
            HideLoadingOverlay();
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
                OpenItemLensImmediate();
        });
    }

    private void WarmItemLensWindow()
    {
        if (_itemLensReady && _itemLens != null) return;
        _itemLens = CreateItemLens();
        _itemLens.ApplyOpacity();
        _itemLens.ApplyPlacement();
        _itemLens.PrepareHidden();
        _itemLensReady = true;
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
                PushMarkersToViews();
            }
            _overlay?.RefreshQuestList();
            UpdateMaximizeGlyph();
            RebuildSideList();
            SyncPoiMasters();
            SyncPanelTabs();
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
        App.Items.ItemsUpdated += () => Dispatcher.Invoke(() =>
        {
            var pois = App.Maps.PoisFor(App.Settings.SelectedMapId);
            PreviewMap.SetPois(pois);
            _overlay?.SetPois(pois);
            if (_listMode == "pois") RebuildSideList();
        });
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
        PreviewMap.SetPois(App.Maps.PoisFor(map.Id));
        PreviewMap.SetWaypoint(App.Settings.ActiveWaypoint);
        PreviewMap.SetFollow(App.Settings.FollowPlayer);
        PreviewMap.SetAutoFloor(App.Settings.AutoFloorFromHeight);
        if (_overlay != null)
        {
            _overlay.QuestSelectionChanged -= OnOverlayQuestSelectionChanged;
            _overlay.WaypointRequested -= OnOverlayWaypointRequested;
            _overlay.LayersChanged -= OnOverlayLayersChanged;
            _overlay.PoiFilterChanged -= OnOverlayPoiFilterChanged;
            _overlay.LoadMap(map, extracts, mines, spawns, labels, quests);
            _overlay.SetFollow(App.Settings.FollowPlayer);
            _overlay.SetGlassOpacity(App.Settings.OverlayOpacity);
            _overlay.QuestSelectionChanged += OnOverlayQuestSelectionChanged;
            _overlay.WaypointRequested += OnOverlayWaypointRequested;
            _overlay.LayersChanged += OnOverlayLayersChanged;
            _overlay.PoiFilterChanged += OnOverlayPoiFilterChanged;
        }
        MapTitle.Text = map.Name.ToUpperInvariant();
        SyncPoiMasters();
        ScheduleRebuildQuestList();
    }

    private void ScheduleRebuildQuestList()
    {
        Dispatcher.BeginInvoke(RebuildSideList, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void PushMarkersToViews()
    {
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        _overlay?.SetLayers();
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
        foreach (var q in quests.OrderBy(q => Loc.QuestName(q), StringComparer.OrdinalIgnoreCase))
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
        RebuildSideList();
    }

    private void OnQuestStateChanged()
    {
        if (_suppress) return;
        PushMarkersToViews();
        PreviewMap.SetWaypoint(App.Settings.ActiveWaypoint);
        RebuildSideList();
        _overlay?.RefreshQuestList();
        _overlay?.SetWaypoint(App.Settings.ActiveWaypoint);
    }

    private void OnOverlayQuestSelectionChanged()
    {
        RebuildSideList();
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
        SyncPoiMasters();
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        if (_listMode == "pois") RebuildSideList();
    }

    private void OnOverlayPoiFilterChanged()
    {
        SyncPoiMasters();
        PreviewMap.SetPoiFilter();
        if (_listMode == "pois") RebuildSideList();
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
        _overlay?.SetLayers();
    }

    private void OnPoiCategoryToggled(string category)
    {
        PoiCatalog.ToggleCategoryFromOverlay(category, PresentPoiTypes());
        SettingsStore.Save(App.Settings);
        AfterPoiChange();
    }

    private HashSet<string> PresentPoiTypes() =>
        App.Maps.PoisFor(App.Settings.SelectedMapId)
            .Select(p => p.Type)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private void SyncPoiMasters()
    {
        var present = PresentPoiTypes();
        _suppress = true;
        ShowLootBox.IsChecked = PoiCatalog.CategoryState(PoiCatalog.CatLoot, present);
        ShowBossesBox.IsChecked = PoiCatalog.CategoryState(PoiCatalog.CatEnemies, present);
        ShowLocsBox.IsChecked = PoiCatalog.CategoryState(PoiCatalog.CatLocations, present);
        _suppress = false;
    }

    private void AfterPoiChange()
    {
        if (_poiBusy) return;
        _poiBusy = true;
        try
        {
            SyncPoiMasters();
            PreviewMap.SetPoiFilter();
            _overlay?.SetPoiFilter();
            if (_listMode == "pois") RebuildSideList();
        }
        finally
        {
            _poiBusy = false;
        }
    }

    private void PoiMaster_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (_suppress || _poiBusy || sender is not CheckBox box) return;
        var cat = ReferenceEquals(box, ShowLootBox) ? PoiCatalog.CatLoot
            : ReferenceEquals(box, ShowBossesBox) ? PoiCatalog.CatEnemies
            : PoiCatalog.CatLocations;
        PoiCatalog.ToggleCategory(cat, PresentPoiTypes());
        SettingsStore.Save(App.Settings);
        AfterPoiChange();
    }

    private void PoiPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag }) return;
        if (tag == "clean") PoiCatalog.ClearAll();
        else if (tag == "raid") PoiCatalog.ApplyPreset(PoiCatalog.RaidPreset);
        else if (tag == "loot") PoiCatalog.ApplyPreset(PoiCatalog.LootRunPreset);
        SettingsStore.Save(App.Settings);
        AfterPoiChange();
    }

    private void PanelTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string mode }) return;
        _listMode = mode;
        SyncPanelTabs();
        RebuildSideList();
    }

    private void SyncPanelTabs()
    {
        var active = (Style)FindResource("PanelTabButtonActive");
        var idle = (Style)FindResource("PanelTabButton");
        PanelQuestsBtn.Style = _listMode == "quests" ? active : idle;
        PanelPoisBtn.Style = _listMode == "pois" ? active : idle;
        QuestSearchBox.ToolTip = _listMode == "pois"
            ? Loc.T("Main.Poi.Search.Tooltip")
            : Loc.T("Main.Quests.Search.Tooltip");
        if (PanelHintText != null)
            PanelHintText.Text = _listMode == "pois" ? Loc.T("Main.Poi.Hint") : Loc.T("Main.Quests.Hint");
    }

    private void RebuildSideList()
    {
        if (_listMode == "pois")
        {
            QuestListPanel.Children.Clear();
            QuestListPanel.Children.Add(
                PoiListUi.BuildCatalog(App.Settings.SelectedMapId, QuestSearchBox.Text, AfterPoiChange));
            return;
        }
        RebuildQuestList();
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

    private async void Settings_Click(object sender, RoutedEventArgs e)
    {
        PreviewMap.SetHwndHidden(true);
        await Dispatcher.InvokeAsync(static () => { }, System.Windows.Threading.DispatcherPriority.Render);
        CaptureSettingsBlur();
        SettingsHost.LoadFromSettings();
        SettingsOverlay.Visibility = Visibility.Visible;
        var anim = new DoubleAnimation(440, 0, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        SettingsDrawerShift.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private void CaptureSettingsBlur()
    {
        try
        {
            AppChrome.UpdateLayout();
            var w = (int)Math.Ceiling(AppChrome.ActualWidth);
            var h = (int)Math.Ceiling(AppChrome.ActualHeight);
            if (w < 1 || h < 1)
            {
                SettingsBlurShot.Source = null;
                return;
            }

            var shot = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            shot.Render(AppChrome);
            shot.Freeze();
            SettingsBlurShot.Source = shot;
        }
        catch
        {
            SettingsBlurShot.Source = null;
        }
    }

    private void SettingsScrim_Click(object sender, MouseButtonEventArgs e)
    {
        HideSettingsDrawer();
        e.Handled = true;
    }

    private void SettingsDrawer_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void HideSettingsDrawer()
    {
        if (SettingsOverlay.Visibility != Visibility.Visible) return;
        var anim = new DoubleAnimation(0, 440, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
        };
        anim.Completed += (_, _) =>
        {
            SettingsOverlay.Visibility = Visibility.Collapsed;
            SettingsBlurShot.Source = null;
            PreviewMap.SetHwndHidden(false);
        };
        SettingsDrawerShift.BeginAnimation(TranslateTransform.XProperty, anim);
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
        SyncPoiMasters();
        SyncPanelTabs();
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
        NativeMethods.RegisterHotKey(hwnd, 11, NativeMethods.ModNorepeat, NativeMethods.VkPrior);
        NativeMethods.RegisterHotKey(hwnd, 12, NativeMethods.ModNorepeat, NativeMethods.VkNext);
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
                NativeMethods.UnregisterHotKey(hwnd, 11);
                NativeMethods.UnregisterHotKey(hwnd, 12);
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
                Dispatcher.BeginInvoke(ToggleItemLensCore, System.Windows.Threading.DispatcherPriority.Input);
                handled = true;
            }
            else if (id == 11)
            {
                ShiftMapFloor(1);
                handled = true;
            }
            else if (id == 12)
            {
                ShiftMapFloor(-1);
                handled = true;
            }
        }

        return IntPtr.Zero;
    }

    private void ShiftMapFloor(int delta)
    {
        PreviewMap.ShiftFloor(delta);
        _overlay?.ShiftFloor(delta);
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
            if (!shift) return;

            BeginItemLensScan();
            App.ItemScan.ScanIconAt(x, y, _itemScanGeneration);
        };

        App.ItemScan.ScanCompleted += r => _ = OnItemScanCompletedAsync(r);
        App.ItemScan.ScanFailed += msg => _ = OnItemScanFailedAsync(msg);
        App.ItemScan.StatusChanged += msg =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_itemLensOpening || !IsItemLensRevealed()) return;
                _itemLens!.SetStatus(msg, loading: true);
                if (string.Equals(msg, Loc.T("ItemScan.Status.Indexing"), StringComparison.Ordinal))
                    _itemLens.ShowLoading(msg, Loc.T("ItemScan.Status.IndexingHint"));
            });
        };
    }

    private bool IsItemLensRevealed() =>
        _itemLens?.IsRevealed == true && App.Settings.ItemLensVisible;

    private void CancelPendingItemLensScans()
    {
        _itemScanGeneration++;
        _shiftScanPending = false;
        App.ItemScan.CancelPendingScans();
    }

    private void BeginItemLensScan()
    {
        _shiftScanPending = true;
        _itemScanGeneration++;
        _itemScanStartedUtc = DateTime.UtcNow;
        _itemLensOpening = false;
        _itemLensAcceptsScans = true;
        if (!_itemLensReady) WarmItemLensWindow();
        _itemLens!.ShowLoading(Loc.T("ItemScan.Status.Scanning"), Loc.T("ItemScan.Status.ScanningHint"));
        _itemLens.RevealInstant();
        App.Settings.ItemLensVisible = true;
    }

    private async Task OnItemScanCompletedAsync(ItemScanResult result)
    {
        if (result.ScanId != _itemScanGeneration) return;

        if (_shiftScanPending)
        {
            _shiftScanPending = false;
            await FinishItemLensScanAsync(result);
            return;
        }

        if (!App.Settings.ItemLensVisible) return;
        if (_itemLensOpening || !_itemLensAcceptsScans) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (result.ScanId != _itemScanGeneration) return;
            if (!App.Settings.ItemLensVisible || _itemLensOpening) return;
            if (!_itemLensReady) WarmItemLensWindow();
            _itemLens!.ShowResult(result);
            _itemLens.RevealInstant();
        });
    }

    private async Task OnItemScanFailedAsync(string message)
    {
        if (_shiftScanPending)
        {
            _shiftScanPending = false;
            await FinishItemLensScanFailedAsync(message);
            return;
        }

        if (!App.Settings.ItemLensVisible) return;
        if (_itemLensOpening || !_itemLensAcceptsScans) return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (!App.Settings.ItemLensVisible || _itemLensOpening) return;
            if (!_itemLensReady) WarmItemLensWindow();
            _itemLens!.ShowScanFailed(message);
            _itemLens.RevealInstant();
        });
    }

    private async Task FinishItemLensScanAsync(ItemScanResult result)
    {
        var gen = result.ScanId;
        await WaitMinScanLoadingAsync().ConfigureAwait(false);

        await Dispatcher.InvokeAsync(() =>
        {
            if (gen != _itemScanGeneration || !App.Settings.ItemLensVisible) return;
            if (!_itemLensReady) WarmItemLensWindow();
            _itemLens!.ShowResult(result);
            _itemLens.RevealInstant();
        });
    }

    private async Task FinishItemLensScanFailedAsync(string message)
    {
        var gen = _itemScanGeneration;
        await WaitMinScanLoadingAsync().ConfigureAwait(false);

        await Dispatcher.InvokeAsync(() =>
        {
            if (gen != _itemScanGeneration || !App.Settings.ItemLensVisible) return;
            if (!_itemLensReady) WarmItemLensWindow();
            _itemLens!.ShowScanFailed(message);
            _itemLens.RevealInstant();
        });
    }

    private async Task WaitMinScanLoadingAsync()
    {
        var wait = MinItemScanLoading - (DateTime.UtcNow - _itemScanStartedUtc);
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait).ConfigureAwait(false);
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
        win.Concealed += () =>
        {
            if (!App.Settings.ItemLensVisible) return;
            App.Settings.ItemLensVisible = false;
            _ = Task.Run(() => SettingsStore.Save(App.Settings));
        };
        return win;
    }

    private void OpenItemLensImmediate()
    {
        if (!_itemLensReady || _itemLens == null)
            return;

        CancelPendingItemLensScans();
        var gen = ++_itemLensOpenGeneration;
        _itemLensOpening = true;
        _itemLensAcceptsScans = false;
        App.Settings.ItemLensVisible = true;
        _itemLens.ShowOpening();
        _itemLens.RevealInstant();
        _ = FinishItemLensOpenAsync(gen);
    }

    private async Task FinishItemLensOpenAsync(int gen)
    {
        await _itemLens!.WaitMinLoadingVisibleAsync(MinItemLensOpenLoading).ConfigureAwait(false);

        await Dispatcher.InvokeAsync(() =>
        {
            _itemLensOpening = false;
            if (gen != _itemLensOpenGeneration || !App.Settings.ItemLensVisible) return;
            if (_itemLens?.IsRevealed != true) return;

            _itemLensAcceptsScans = true;

            if (!App.ItemScan.IndexReady)
            {
                _itemLens.ShowLoading(
                    Loc.T("ItemScan.Status.Indexing"),
                    Loc.T("ItemScan.Status.IndexingHint"));
                _ = App.ItemScan.EnsureReadyAsync();
            }
            else
            {
                _itemLens.ShowEmpty();
            }
        }, System.Windows.Threading.DispatcherPriority.Background);

        _ = Task.Run(() => SettingsStore.Save(App.Settings));
    }

    private void CloseItemLensImmediate()
    {
        _itemLensOpenGeneration++;
        _itemLensOpening = false;
        _itemLensAcceptsScans = false;
        CancelPendingItemLensScans();
        App.Settings.ItemLensVisible = false;
        _itemLens?.Conceal();
        _ = Task.Run(() => SettingsStore.Save(App.Settings));
    }

    private void ToggleItemLensCore()
    {
        if (_itemLens?.IsRevealed == true)
            CloseItemLensImmediate();
        else
            OpenItemLensImmediate();
    }

    private void ToggleItemLens() => ToggleItemLensCore();

    private void PlaceItemLensDefault()
    {
        if (_itemLens == null) return;
        var wa = SystemParameters.WorkArea;
        _itemLens.Left = wa.Left + 16;
        _itemLens.Top = wa.Top + 16;
    }

    private void ShowItemLens() => OpenItemLensImmediate();

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
