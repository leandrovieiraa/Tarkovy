using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class OverlayWindow : Window
{
    public bool IsExpanded { get; private set; }
    public bool IsSidePanelVisible { get; private set; }
    private IReadOnlyList<ExtractMarker> _extracts = [];
    private IReadOnlyList<QuestDefinition> _quests = [];

    public event Action? QuestSelectionChanged;
    public event Action<MapWaypoint?>? WaypointRequested;
    public event Action? LayersChanged;

    public OverlayWindow()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            ApplyMiniLayout();
            OverlayMap.ResetView();
        };
        SizeChanged += (_, e) =>
        {
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
        OverlayMap.SetWaypoint(App.Settings.ActiveWaypoint);
        OverlayMap.SetFollow(App.Settings.FollowPlayer);
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

    public void SetQuests(IReadOnlyList<QuestDefinition> quests)
    {
        _quests = quests;
        OverlayMap.SetQuests(quests, QuestState.TrackingSlugs());
        RebuildSidePanel();
    }

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
        if (IsExpanded) ApplyMiniLayout();
        else ApplyExpandedLayout();
    }

    public void ToggleSidePanel()
    {
        if (!IsExpanded)
        {
            ApplyExpandedLayout();
            return;
        }

        SetSidePanelVisible(!IsSidePanelVisible);
        OverlayMap.ResetView();
    }

    private void PanelToggle_Click(object sender, RoutedEventArgs e) => ToggleSidePanel();

    private void SetSidePanelVisible(bool visible)
    {
        IsSidePanelVisible = visible;
        if (visible)
        {
            ExtractCol.Width = new GridLength(240);
            ExtractPanel.Visibility = Visibility.Visible;
        }
        else
        {
            ExtractCol.Width = new GridLength(0);
            ExtractPanel.Visibility = Visibility.Collapsed;
        }
        SyncPanelToggleButton();
    }

    private void SyncPanelToggleButton()
    {
        if (PanelToggleBtn == null) return;
        if (!IsExpanded)
        {
            PanelToggleBtn.Visibility = Visibility.Collapsed;
            return;
        }

        PanelToggleBtn.Visibility = Visibility.Visible;
        PanelToggleBtn.Content = IsSidePanelVisible ? "«" : "»";
        PanelToggleBtn.ToolTip = IsSidePanelVisible
            ? Loc.T("Overlay.Tooltip.HidePanel")
            : Loc.T("Overlay.Tooltip.ShowPanel");
    }

    private bool _placedOnce;

    public void ApplyMiniLayout()
    {
        IsExpanded = false;
        IsSidePanelVisible = false;
        Width = 320;
        Height = 348;
        ExtractCol.Width = new GridLength(0);
        ExtractPanel.Visibility = Visibility.Collapsed;
        SyncPanelToggleButton();
        if (!_placedOnce)
        {
            PlaceBottomRight(16);
            _placedOnce = true;
        }
        NativeMethods.SetClickThrough(this, false);
    }

    public void ApplyExpandedLayout()
    {
        IsExpanded = true;
        Width = 920;
        Height = 560;
        SetSidePanelVisible(true);
        NativeMethods.SetClickThrough(this, false);
    }

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
