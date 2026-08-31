using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Tarkovy.Services;

namespace Tarkovy;

public partial class SettingsWindow : UserControl
{
    private bool _suppressLang;
    private string _langWhenOpened = Loc.English;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public void LoadFromSettings()
    {
        var s = App.Settings;
        _suppressLang = true;
        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = Loc.T("Settings.Language.English"),
            Tag = Loc.English,
            Foreground = (Brush)FindResource("BrushText"),
            Background = (Brush)FindResource("BrushBgRaised")
        });
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = Loc.T("Settings.Language.Portuguese"),
            Tag = Loc.Portuguese,
            Foreground = (Brush)FindResource("BrushText"),
            Background = (Brush)FindResource("BrushBgRaised")
        });
        LanguageCombo.SelectedIndex = Loc.Normalize(s.UiLanguage) == Loc.Portuguese ? 1 : 0;
        _suppressLang = false;

        _langWhenOpened = Loc.Normalize(s.UiLanguage);
        StartWindowsBox.IsChecked = s.StartWithWindows;
        LogsPathBox.Text = s.LogsFolder;
        ShotsPathBox.Text = s.ScreenshotsFolder;
        ShowExtractsBox.IsChecked = s.ShowExtracts;
        ShowMinesBox.IsChecked = s.ShowMines;
        ShowSpawnsBox.IsChecked = s.ShowSpawns;
        ShowLabelsBox.IsChecked = s.ShowMarkerLabels;
        OpacitySlider.Value = s.OverlayOpacity;
        FollowBox.IsChecked = s.FollowPlayer;
        AutoFloorBox.IsChecked = s.AutoFloorFromHeight;
        DeleteBox.IsChecked = s.DeleteAfterRead;
        KeepLastBox.IsChecked = s.KeepLastScreenshot;
        AutoCleanupBox.IsChecked = s.AutoCleanupOnRaidEnd;
        ItemScanBox.IsChecked = s.ItemScanEnabled;
        ItemLensOpacitySlider.Value = s.ItemLensOpacity;
        ItemScanDebugBox.IsChecked = s.ItemScanDebugEnabled;
        ItemScanDebugPathBox.Text = ItemScanDebug.RootDir;
        ItemScanAiBox.IsChecked = s.ItemScanAiEnabled;
        ItemScanAiKeyBox.Password = s.ItemScanAiApiKey ?? "";
        PopulateItemScanAiProviderCombo(s.ItemScanAiProvider);
        PopulateItemScanSlotCombo(s.ItemScanSlotPx);
        RefreshPathBadges();
    }

    private void PopulateItemScanSlotCombo(int selectedPx)
    {
        ItemScanSlotCombo.Items.Clear();
        ItemScanSlotCombo.Items.Add(MakeSlotItem(Loc.T("Settings.ItemScanSlot.Auto"), 0));
        ItemScanSlotCombo.Items.Add(MakeSlotItem(Loc.T("Settings.ItemScanSlot.1080"), 63));
        ItemScanSlotCombo.Items.Add(MakeSlotItem(Loc.T("Settings.ItemScanSlot.1440"), 84));
        ItemScanSlotCombo.Items.Add(MakeSlotItem(Loc.T("Settings.ItemScanSlot.4k"), 126));
        ItemScanSlotCombo.SelectedIndex = selectedPx switch
        {
            63 => 1,
            84 => 2,
            126 => 3,
            _ => 0
        };
    }

    private void PopulateItemScanAiProviderCombo(string? selected)
    {
        var tag = (selected ?? "claude").Trim().ToLowerInvariant();
        ItemScanAiProviderCombo.Items.Clear();
        ItemScanAiProviderCombo.Items.Add(MakeAiProvider(Loc.T("Settings.ItemScanAi.Claude"), "claude"));
        ItemScanAiProviderCombo.Items.Add(MakeAiProvider(Loc.T("Settings.ItemScanAi.Cursor"), "cursor"));
        ItemScanAiProviderCombo.Items.Add(MakeAiProvider(Loc.T("Settings.ItemScanAi.OpenAi"), "openai"));
        ItemScanAiProviderCombo.Items.Add(MakeAiProvider(Loc.T("Settings.ItemScanAi.Gemini"), "gemini"));
        ItemScanAiProviderCombo.SelectedIndex = tag switch
        {
            "cursor" => 1,
            "openai" => 2,
            "gemini" => 3,
            _ => 0
        };
        RefreshAiKeyHint();
    }

    private void ItemScanAiProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshAiKeyHint();
    }

    private void RefreshAiKeyHint()
    {
        var tag = ItemScanAiProviderCombo.SelectedItem is ComboBoxItem item && item.Tag is string t ? t : "claude";
        ItemScanAiKeyHintText.Text = tag == "cursor"
            ? Loc.T("Settings.ItemScanAiKeyHint.Cursor")
            : Loc.T("Settings.ItemScanAiKeyHint");
    }

    private static ComboBoxItem MakeAiProvider(string label, string tag) =>
        new() { Content = label, Tag = tag };

    private static ComboBoxItem MakeSlotItem(string label, int px) =>
        new() { Content = label, Tag = px };

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressLang || !IsLoaded) return;
        if (LanguageCombo.SelectedItem is not ComboBoxItem item || item.Tag is not string code) return;
        Loc.Apply(code);
        // Refresh combo labels after dictionary swap
        _suppressLang = true;
        var idx = LanguageCombo.SelectedIndex;
        LanguageCombo.Items.Clear();
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = Loc.T("Settings.Language.English"),
            Tag = Loc.English,
            Foreground = (Brush)FindResource("BrushText"),
            Background = (Brush)FindResource("BrushBgRaised")
        });
        LanguageCombo.Items.Add(new ComboBoxItem
        {
            Content = Loc.T("Settings.Language.Portuguese"),
            Tag = Loc.Portuguese,
            Foreground = (Brush)FindResource("BrushText"),
            Background = (Brush)FindResource("BrushBgRaised")
        });
        LanguageCombo.SelectedIndex = idx;
        _suppressLang = false;
        PopulateItemScanSlotCombo(App.Settings.ItemScanSlotPx);
        PopulateItemScanAiProviderCombo(App.Settings.ItemScanAiProvider);
        RefreshAiKeyHint();
        RefreshPathBadges();
        _ = ItemLocalizedNames.ReloadAsync();
    }

    private void RefreshPathBadges()
    {
        var logsOk = Directory.Exists(LogsPathBox.Text);
        LogsOk.Text = logsOk ? Loc.T("Settings.Path.Ok") : Loc.T("Settings.Path.Missing");
        LogsOk.Foreground = logsOk ? (Brush)FindResource("BrushOk") : (Brush)FindResource("BrushErr");
        var shotsOk = Directory.Exists(ShotsPathBox.Text);
        ShotsOk.Text = shotsOk ? Loc.T("Settings.Path.Ok") : Loc.T("Settings.Path.Missing");
        ShotsOk.Foreground = shotsOk ? (Brush)FindResource("BrushOk") : (Brush)FindResource("BrushErr");
    }

    private void BrowseLogs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Settings.Dialog.Logs") };
        if (dlg.ShowDialog() == true)
        {
            LogsPathBox.Text = dlg.FolderName;
            RefreshPathBadges();
        }
    }

    private void BrowseShots_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = Loc.T("Settings.Dialog.Shots") };
        if (dlg.ShowDialog() == true)
        {
            ShotsPathBox.Text = dlg.FolderName;
            RefreshPathBadges();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var s = App.Settings;
        if (LanguageCombo.SelectedItem is ComboBoxItem langItem && langItem.Tag is string code)
            s.UiLanguage = Loc.Normalize(code);
        s.StartWithWindows = StartWindowsBox.IsChecked == true;
        s.LogsFolder = LogsPathBox.Text.Trim();
        s.ScreenshotsFolder = ShotsPathBox.Text.Trim();
        s.ShowExtracts = ShowExtractsBox.IsChecked == true;
        s.ShowMines = ShowMinesBox.IsChecked == true;
        s.ShowSpawns = ShowSpawnsBox.IsChecked == true;
        s.ShowMarkerLabels = ShowLabelsBox.IsChecked == true;
        s.OverlayOpacity = OpacitySlider.Value;
        s.FollowPlayer = FollowBox.IsChecked == true;
        s.AutoFloorFromHeight = AutoFloorBox.IsChecked == true;
        s.DeleteAfterRead = DeleteBox.IsChecked == true;
        s.KeepLastScreenshot = KeepLastBox.IsChecked == true;
        s.AutoCleanupOnRaidEnd = AutoCleanupBox.IsChecked == true;
        s.ItemScanEnabled = ItemScanBox.IsChecked == true;
        s.ItemLensOpacity = ItemLensOpacitySlider.Value;
        s.ItemScanDebugEnabled = ItemScanDebugBox.IsChecked == true;
        s.ItemScanAiEnabled = ItemScanAiBox.IsChecked == true;
        s.ItemScanAiApiKey = ItemScanAiKeyBox.Password?.Trim() ?? "";
        if (ItemScanAiProviderCombo.SelectedItem is ComboBoxItem aiItem && aiItem.Tag is string provider)
            s.ItemScanAiProvider = provider;
        if (ItemScanSlotCombo.SelectedItem is ComboBoxItem slotItem && slotItem.Tag is int slotPx)
            s.ItemScanSlotPx = slotPx;
        var langChanged = Loc.Normalize(s.UiLanguage) != _langWhenOpened;
        Loc.Apply(s.UiLanguage);
        if (langChanged)
            _ = ItemLocalizedNames.ReloadAsync();
        _ = Task.Run(() => StartupRegistration.Apply(s.StartWithWindows));
        App.ApplyWatchers();
        SettingsStore.Save(s);
        SettingsApplied?.Invoke();
        Closed?.Invoke();
    }

    public static event Action? SettingsApplied;
    public event Action? Closed;

    private void OpenItemScanDebug_Click(object sender, RoutedEventArgs e)
    {
        var dir = ItemScanDebug.RootDir;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo
        {
            FileName = dir,
            UseShellExecute = true
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}
