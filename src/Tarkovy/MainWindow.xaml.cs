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
        foreach (var map in App.Maps.Maps)
            MapCombo.Items.Add(new ComboBoxItem
            {
                Content = map.Name,
                Tag = map.Id,
                Foreground = (System.Windows.Media.Brush)FindResource("BrushText"),
                Background = (System.Windows.Media.Brush)FindResource("BrushBgRaised")
            });
        SelectMapId(App.Settings.SelectedMapId);
        ShowExtractsBox.IsChecked = App.Settings.ShowExtracts;
        ShowMinesBox.IsChecked = App.Settings.ShowMines;
        ShowLabelsBox.IsChecked = App.Settings.ShowMarkerLabels;
        _suppress = false;

        WireRuntime();
        SettingsWindow.SettingsApplied += OnSettingsApplied;
        Loc.LanguageChanged += OnLanguageChanged;
        RegisterHotkeys();
        if (App.Settings.OverlayVisible)
            ShowOverlay();
        PushMapToViews();
        RefreshHud();
    }

    private void OnLanguageChanged()
    {
        Dispatcher.Invoke(() =>
        {
            PreviewMap.ApplyLanguage();
            _overlay?.ApplyLanguage();
            UpdateMaximizeGlyph();
            RefreshHud();
        });
    }

    private void WireRuntime()
    {
        App.Logs.MapDetected += map => Dispatcher.Invoke(() =>
        {
            App.Raid.SetMap(map);
            App.Settings.SelectedMapId = map.Id;
            SelectMapId(map.Id);
            PushMapToViews();
            RefreshHud();
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
        var extracts = App.Settings.ShowExtracts ? App.Maps.ExtractsFor(map.Id) : [];
        var mines = App.Settings.ShowMines ? App.Maps.MinesFor(map.Id) : [];
        var labels = App.Settings.ShowMarkerLabels;
        PreviewMap.LoadMap(map, extracts, mines, labels);
        PreviewMap.SetFollow(App.Settings.FollowPlayer);
        _overlay?.LoadMap(map, extracts, mines, labels);
        _overlay?.SetFollow(App.Settings.FollowPlayer);
        _overlay?.SetGlassOpacity(App.Settings.OverlayOpacity);
        MapTitle.Text = map.Name.ToUpperInvariant();
    }

    private void RefreshHud()
    {
        StatusBadge.Text = App.Raid.StatusLabel;
        StatusBadge.Foreground = App.Raid.Status == RaidStatus.InRaid
            ? (Brush)FindResource("BrushAmberHot")
            : (Brush)FindResource("BrushAmber");
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
        SettingsStore.Save(App.Settings);
        PushMapToViews();
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
        _suppress = false;
        PushMapToViews();
        RefreshHud();
    }

    private void MapCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress) return;
        if (MapCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string id) return;
        App.Settings.SelectedMapId = id;
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
