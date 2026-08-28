using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Tarkovy.Services;

namespace Tarkovy;

public partial class SettingsWindow : Window
{
    private bool _suppressLang;

    public SettingsWindow()
    {
        InitializeComponent();
        NativeMethods.EnableWorkAreaMaximize(this);
        Loaded += (_, _) => LoadFromSettings();
    }

    private void LoadFromSettings()
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

        StartWindowsBox.IsChecked = s.StartWithWindows || StartupRegistration.IsEnabled();
        LogsPathBox.Text = s.LogsFolder;
        ShotsPathBox.Text = s.ScreenshotsFolder;
        ShowExtractsBox.IsChecked = s.ShowExtracts;
        ShowMinesBox.IsChecked = s.ShowMines;
        ShowLabelsBox.IsChecked = s.ShowMarkerLabels;
        OpacitySlider.Value = s.OverlayOpacity;
        FollowBox.IsChecked = s.FollowPlayer;
        DeleteBox.IsChecked = s.DeleteAfterRead;
        KeepLastBox.IsChecked = s.KeepLastScreenshot;
        AutoCleanupBox.IsChecked = s.AutoCleanupOnRaidEnd;
        RefreshPathBadges();
    }

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
        Title = Loc.T("Settings.Title");
        RefreshPathBadges();
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
        s.ShowMarkerLabels = ShowLabelsBox.IsChecked == true;
        s.OverlayOpacity = OpacitySlider.Value;
        s.FollowPlayer = FollowBox.IsChecked == true;
        s.DeleteAfterRead = DeleteBox.IsChecked == true;
        s.KeepLastScreenshot = KeepLastBox.IsChecked == true;
        s.AutoCleanupOnRaidEnd = AutoCleanupBox.IsChecked == true;
        Loc.Apply(s.UiLanguage);
        StartupRegistration.Apply(s.StartWithWindows);
        App.ApplyWatchers();
        SettingsStore.Save(s);
        SettingsApplied?.Invoke();
        Close();
    }

    public static event Action? SettingsApplied;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
