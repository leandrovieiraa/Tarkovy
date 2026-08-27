using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class MainWindow : Window
{
    private OverlayWindow? _overlay;
    private HwndSource? _hotkeySource;
    private bool _suppress;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Closed += (_, _) =>
        {
            UnregisterHotkeys();
            SettingsWindow.SettingsApplied -= OnSettingsApplied;
            Loc.LanguageChanged -= OnLanguageChanged;
            _overlay?.Close();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _suppress = true;
        AppVersionLabel.Text = ProductInfo.AppVersionLabel;
        RefreshEftTargetLabel();
        PopulateMapCombo();
        SelectMapId(App.Settings.SelectedMapId);
        ShowExtractsBox.IsChecked = App.Settings.ShowExtracts;
        ShowMinesBox.IsChecked = App.Settings.ShowMines;
        ShowLabelsBox.IsChecked = App.Settings.ShowMarkerLabels;
        ShowQuestsBox.IsChecked = App.Settings.ShowQuests;
        _suppress = false;

        WireRuntime();
        PreviewMap.LayerToggled += OnMapLayerToggled;
        PreviewMap.WaypointChanged += wp => App.Settings.ActiveWaypoint = wp;
        SettingsWindow.SettingsApplied += OnSettingsApplied;
        Loc.LanguageChanged += OnLanguageChanged;
        RegisterHotkeys();
        if (App.Settings.OverlayVisible)
            ShowOverlay();
        PushMapToViews();
        RefreshHud();
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
                _overlay.ApplyExpandedLayout();
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
        var quests = App.Maps.QuestsFor(map.Id);
        var labels = App.Settings.ShowMarkerLabels;
        PreviewMap.LoadMap(map, extracts, mines, labels);
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        PreviewMap.SetQuests(quests, App.Settings.EnabledQuestSlugs);
        PreviewMap.SetWaypoint(App.Settings.ActiveWaypoint);
        PreviewMap.SetFollow(App.Settings.FollowPlayer);
        if (_overlay != null)
        {
            _overlay.QuestSelectionChanged -= OnOverlayQuestSelectionChanged;
            _overlay.WaypointRequested -= OnOverlayWaypointRequested;
            _overlay.LayersChanged -= OnOverlayLayersChanged;
            _overlay.LoadMap(map, extracts, mines, labels, quests);
            _overlay.SetFollow(App.Settings.FollowPlayer);
            _overlay.SetGlassOpacity(App.Settings.OverlayOpacity);
            _overlay.QuestSelectionChanged += OnOverlayQuestSelectionChanged;
            _overlay.WaypointRequested += OnOverlayWaypointRequested;
            _overlay.LayersChanged += OnOverlayLayersChanged;
        }
        MapTitle.Text = map.Name.ToUpperInvariant();
        RebuildQuestList();
    }

    private void PushMarkersToViews()
    {
        var map = App.Maps.FindById(App.Settings.SelectedMapId) ?? App.Maps.Maps.FirstOrDefault();
        if (map == null) return;
        var extracts = App.Maps.ExtractsFor(map.Id);
        var mines = App.Maps.MinesFor(map.Id);
        var quests = App.Maps.QuestsFor(map.Id);
        var labels = App.Settings.ShowMarkerLabels;
        PreviewMap.SetMarkers(extracts, mines, labels);
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        PreviewMap.SetQuests(quests, App.Settings.EnabledQuestSlugs);
        _overlay?.SetMarkers(extracts, mines, labels);
        _overlay?.SetQuests(quests);
    }

    private sealed class QuestRowTag
    {
        public required string MapId { get; init; }
        public required string MapName { get; init; }
        public required string Slug { get; init; }
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

        var enabled = new HashSet<string>(App.Settings.EnabledQuestSlugs, StringComparer.OrdinalIgnoreCase);
        foreach (var q in quests.OrderBy(q => q.Name, StringComparer.OrdinalIgnoreCase))
        {
            var label = string.IsNullOrWhiteSpace(Loc.QuestTrader(q))
                ? Loc.QuestName(q)
                : $"{Loc.QuestName(q)}  ·  {Loc.QuestTrader(q)}";
            var box = new CheckBox
            {
                Content = label,
                IsChecked = enabled.Contains(q.Slug),
                Margin = new Thickness(0, 0, 0, 4),
                ToolTip = Loc.T("Main.Quests.MapTooltip", map.Name),
                Tag = new QuestRowTag { MapId = map.Id, MapName = map.Name, Slug = q.Slug }
            };
            box.Checked += QuestToggle_Changed;
            box.Unchecked += QuestToggle_Changed;
            QuestListPanel.Children.Add(box);
        }
    }

    private void QuestToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || sender is not CheckBox { Tag: QuestRowTag tag } box) return;
        if (!string.Equals(tag.MapId, App.Settings.SelectedMapId, StringComparison.OrdinalIgnoreCase))
            return;

        var list = App.Settings.EnabledQuestSlugs;
        if (box.IsChecked == true)
        {
            if (!list.Contains(tag.Slug, StringComparer.OrdinalIgnoreCase))
                list.Add(tag.Slug);
        }
        else
        {
            list.RemoveAll(s => string.Equals(s, tag.Slug, StringComparison.OrdinalIgnoreCase));
        }
        SettingsStore.Save(App.Settings);
        PushMarkersToViews();
    }

    private void OnOverlayQuestSelectionChanged()
    {
        RebuildQuestList();
        var map = App.Maps.FindById(App.Settings.SelectedMapId) ?? App.Maps.Maps.FirstOrDefault();
        if (map == null) return;
        PreviewMap.SetQuests(App.Maps.QuestsFor(map.Id), App.Settings.EnabledQuestSlugs);
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
        ShowLabelsBox.IsChecked = App.Settings.ShowMarkerLabels;
        ShowQuestsBox.IsChecked = App.Settings.ShowQuests;
        _suppress = false;
        PreviewMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
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
        ShowLabelsBox.IsChecked = App.Settings.ShowMarkerLabels;
        ShowQuestsBox.IsChecked = App.Settings.ShowQuests;
        _suppress = false;
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
        _overlay.Show();
        _overlay.ApplyMiniLayout();
        NativeMethods.SetClickThrough(_overlay, clickThrough: false);
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
            _overlay.Show();
            NativeMethods.SetClickThrough(_overlay, clickThrough: false);
            App.Settings.OverlayVisible = true;
        }

        SettingsStore.Save(App.Settings);
    }
}
