using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class OverlayWindow : Window
{
    private const double MiniMapWidth = 320;
    private const double WindowMinWidth = 260;
    private const double WindowMinHeight = 180;
    private const double PanelWidth = 240;
    private const double MiniHeight = 348;

    public bool IsExpanded { get; private set; }
    public bool IsSidePanelVisible { get; private set; }
    private IReadOnlyList<ExtractMarker> _extracts = [];
    private IReadOnlyList<QuestDefinition> _quests = [];

    public event Action? QuestSelectionChanged;
    public event Action<MapWaypoint?>? WaypointRequested;
    public event Action? LayersChanged;
    public event Action? PoiFilterChanged;

    public OverlayWindow()
    {
        InitializeComponent();
        MinWidth = WindowMinWidth;
        MinHeight = WindowMinHeight;
        NativeMethods.EnableWorkAreaMaximize(this);
        WindowPlacementHelper.Wire(this, App.Settings.OverlayWindowPlacement, () =>
        {
            App.Settings.OverlaySidePanelOpen = IsSidePanelVisible;
            SettingsStore.Save(App.Settings);
        });
        Loaded += (_, _) =>
        {
            RestorePlacement();
            OverlayMap.ResetView();
        };
        SizeChanged += (_, e) =>
        {
            EnforceMinSize();
            if (e.PreviousSize.Width > 0 &&
                (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 8 ||
                 Math.Abs(e.NewSize.Height - e.PreviousSize.Height) > 8))
                OverlayMap.ResetView();
        };
        OverlayMap.WaypointChanged += wp =>
        {
            App.Settings.ActiveWaypoint = wp;
            WaypointRequested?.Invoke(wp);
        };
        OverlayMap.LayerToggled += OnLayerToggled;
        OverlayMap.PoiCategoryToggled += OnPoiCategoryToggled;
    }

    private void EnforceMinSize()
    {
        var minW = WindowMinWidth + (IsSidePanelVisible ? PanelWidth : 0);
        var minH = WindowMinHeight;
        MinWidth = minW;
        MinHeight = minH;
        if (Width < MinWidth) Width = MinWidth;
        if (Height < MinHeight) Height = MinHeight;
    }

    private void OnLayerToggled(string key, bool value)
    {
        switch (key)
        {
            case "extracts": App.Settings.ShowExtracts = value; break;
            case "mines": App.Settings.ShowMines = value; break;
            case "spawns": App.Settings.ShowSpawns = value; break;
            case "quests": App.Settings.ShowQuests = value; break;
            case "labels": App.Settings.ShowMarkerLabels = value; break;
            default: return;
        }
        SettingsStore.Save(App.Settings);
        LayersChanged?.Invoke();
    }

    private void OnPoiCategoryToggled(string category)
    {
        var present = App.Maps.PoisFor(App.Settings.SelectedMapId)
            .Select(p => p.Type)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        PoiCatalog.ToggleCategoryFromOverlay(category, present);
        SettingsStore.Save(App.Settings);
        OverlayMap.SetPoiFilter();
        PoiFilterChanged?.Invoke();
    }

    public void SetGlassOpacity(double opacity)
    {
        Opacity = Math.Clamp(opacity, 0.45, 1.0);
        GlassBrush.Color = Color.FromArgb(255, 26, 26, 26);
    }

    public void LoadMap(
        MapDefinition map,
        IReadOnlyList<ExtractMarker> extracts,
        IReadOnlyList<HazardMarker>? mines = null,
        IReadOnlyList<SpawnMarker>? spawns = null,
        bool? showLabels = null,
        IReadOnlyList<QuestDefinition>? quests = null)
    {
        OverlayTitle.Text = map.Name.ToUpperInvariant();
        _extracts = extracts;
        _quests = quests ?? [];
        OverlayMap.LoadMap(map, extracts, mines, spawns, showLabels);
        OverlayMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        OverlayMap.SetQuests(_quests, QuestState.TrackingSlugs());
        OverlayMap.SetPois(App.Maps.PoisFor(map.Id), compact: true);
        OverlayMap.SetWaypoint(App.Settings.ActiveWaypoint);
        OverlayMap.SetFollow(App.Settings.FollowPlayer);
        OverlayMap.SetAutoFloor(App.Settings.AutoFloorFromHeight);
        OverlayMap.ResetView();
        RebuildSidePanel();
    }

    public void ApplyLanguage()
    {
        OverlayMap.ApplyLanguage();
        RebuildSidePanel();
        SyncPanelToggleButton();
    }

    public void SetPlayer(PlayerFix? fix) => OverlayMap.SetPlayer(fix);

    public void SetFollow(bool follow) => OverlayMap.SetFollow(follow);

    public void SetAutoFloor(bool auto) => OverlayMap.SetAutoFloor(auto);

    public void RefreshMapPayload(MapDefinition map) => OverlayMap.RefreshMapPayload(map);

    public void SetMarkers(IReadOnlyList<ExtractMarker> extracts, IReadOnlyList<HazardMarker>? mines = null, IReadOnlyList<SpawnMarker>? spawns = null, bool? showLabels = null)
    {
        _extracts = extracts;
        OverlayMap.SetMarkers(extracts, mines, spawns, showLabels);
        OverlayMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);
        RebuildSidePanel();
    }

    public void SetLayers() =>
        OverlayMap.SetLayers(
            App.Settings.ShowExtracts,
            App.Settings.ShowMines,
            App.Settings.ShowSpawns,
            App.Settings.ShowQuests,
            App.Settings.ShowMarkerLabels);

    public void SetQuests(IReadOnlyList<QuestDefinition> quests)
    {
        _quests = quests;
        OverlayMap.SetQuests(quests, QuestState.TrackingSlugs());
        RebuildSidePanel();
    }

    public void SetPois(IReadOnlyList<MapPoi> pois) =>
        OverlayMap.SetPois(pois, compact: true);

    public void SetPoiFilter() => OverlayMap.SetPoiFilter();

    public void SetWaypoint(MapWaypoint? wp) => OverlayMap.SetWaypoint(wp);

    private void RebuildSidePanel()
    {
        ExtractList.Children.Clear();

        ExtractList.Children.Add(SectionLabel(Loc.T("Overlay.Section.Extracts")));
        foreach (var ex in _extracts)
        {
            var btn = new Button
            {
                Content = Loc.T("Overlay.ExtractPrefix", ex.Name.ToUpperInvariant()),
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(6, 4, 6, 4),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14)),
                Foreground = (Brush)FindResource("BrushText"),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3d, 0xff, 0x7a)),
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = ex
            };
            btn.Click += ExtractWaypoint_Click;
            ExtractList.Children.Add(btn);
        }

        ExtractList.Children.Add(SectionLabel(Loc.T("Overlay.Section.Quests"), top: 12));
        if (_quests.Count == 0)
        {
            ExtractList.Children.Add(new TextBlock
            {
                Text = Loc.T("Overlay.Quests.Empty"),
                Foreground = (Brush)FindResource("BrushTextDim"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
        }
        else
        {
            foreach (var q in _quests.OrderBy(q => Loc.QuestName(q), StringComparer.OrdinalIgnoreCase))
                ExtractList.Children.Add(QuestListUi.BuildRow(q, OnOverlayQuestStateChanged));
        }
    }

    public void RefreshQuestList()
    {
        if (IsExpanded && IsSidePanelVisible)
            RebuildSidePanel();
    }

    private void OnOverlayQuestStateChanged()
    {
        OverlayMap.SetQuests(_quests, QuestState.TrackingSlugs());
        OverlayMap.SetWaypoint(App.Settings.ActiveWaypoint);
        QuestSelectionChanged?.Invoke();
        RebuildSidePanel();
    }

    private static TextBlock SectionLabel(string text, double top = 0) => new()
    {
        Text = text,
        Foreground = (Brush)Application.Current.FindResource("BrushAmber"),
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, top, 0, 6)
    };

    private void ExtractWaypoint_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: ExtractMarker ex }) return;
        var wp = new MapWaypoint
        {
            Kind = "extract",
            Id = ex.Name,
            Name = ex.Name,
            X = ex.X,
            Z = ex.Z
        };
        App.Settings.ActiveWaypoint = wp;
        OverlayMap.SetWaypoint(wp);
        WaypointRequested?.Invoke(wp);
    }

    public void ToggleExpanded()
    {
        if (IsSidePanelVisible)
            SetSidePanelVisible(false);
        else
            OpenSidePanel();
        OverlayMap.ResetView();
    }

    public void ToggleSidePanel() => ToggleExpanded();

    private void OpenSidePanel()
    {
        IsExpanded = true;
        SetSidePanelVisible(true);
        RebuildSidePanel();
        NativeMethods.SetClickThrough(this, false);
    }

    private void PanelToggle_Click(object sender, RoutedEventArgs e) => ToggleSidePanel();

    private void SetSidePanelVisible(bool visible, bool persist = true)
    {
        IsSidePanelVisible = visible;
        IsExpanded = visible;
        if (visible)
        {
            ExtractCol.Width = new GridLength(PanelWidth);
            ExtractPanel.Visibility = Visibility.Visible;
            MinWidth = WindowMinWidth + PanelWidth;
            if (Width < MinWidth) Width = MinWidth;
        }
        else
        {
            ExtractCol.Width = new GridLength(0);
            ExtractPanel.Visibility = Visibility.Collapsed;
            MinWidth = WindowMinWidth;
            if (Width < MinWidth) Width = MinWidth;
        }
        SyncPanelToggleButton();
        EnforceMinSize();
        if (persist)
        {
            App.Settings.OverlaySidePanelOpen = visible;
            WindowPlacementHelper.Capture(this, App.Settings.OverlayWindowPlacement);
            SettingsStore.Save(App.Settings);
        }
    }

    private void SyncPanelToggleButton()
    {
        if (PanelToggleBtn == null) return;
        PanelToggleBtn.Visibility = Visibility.Visible;
        PanelToggleBtn.Content = IsSidePanelVisible ? "«" : "»";
        PanelToggleBtn.ToolTip = IsSidePanelVisible
            ? Loc.T("Overlay.Tooltip.HidePanel")
            : Loc.T("Overlay.Tooltip.ShowPanel");
    }

    public void RestorePlacement()
    {
        var p = App.Settings.OverlayWindowPlacement;
        if (p.IsValid)
        {
            WindowPlacementHelper.Restore(this, p, MiniMapWidth, MiniHeight);
            SetSidePanelVisible(App.Settings.OverlaySidePanelOpen, persist: false);
            IsExpanded = IsSidePanelVisible;
            SyncPanelToggleButton();
        }
        else
            ApplyMiniLayout();

        if (!WindowPlacementHelper.IsOnScreen(this))
            ApplyMiniLayout();

        WindowPlacementHelper.EnsureVisible(this);
        WindowState = WindowState.Normal;
        Topmost = true;
        NativeMethods.SetClickThrough(this, false);
    }

    public void ApplyMiniLayout()
    {
        IsExpanded = false;
        SetSidePanelVisible(false, persist: false);
        Width = MiniMapWidth;
        Height = MiniHeight;
        PlaceBottomRight(16);
        NativeMethods.SetClickThrough(this, false);
    }

    public void ApplyExpandedLayout() => OpenSidePanel();

    private void Header_Drag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left ||
            e.ChangedButton == System.Windows.Input.MouseButton.Right)
        {
            try { DragMove(); }
            catch { /* ignore if button already released */ }
        }
    }

    private void PlaceBottomRight(double margin)
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - margin;
        Top = wa.Bottom - Height - margin;
    }
}
