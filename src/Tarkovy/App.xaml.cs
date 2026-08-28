using System.Windows;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static MapCatalog Maps { get; } = new();
    public static ItemCatalog Items { get; } = new();
    public static ItemScanService ItemScan { get; private set; } = null!;
    public static RaidSession Raid { get; } = new();
    public static LogWatcher Logs { get; private set; } = null!;
    public static ScreenshotWatcher Shots { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Tarkovy", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);
        Settings = SettingsStore.Load();
        SanitizePlacements(Settings);
        QuestState.SanitizeTrackingList();
        Loc.Apply(Settings.UiLanguage);
        var assets = AssetBootstrap.Ensure();
        Maps.LoadBundled(assets);
        ItemScan = new ItemScanService(Items);
        _ = Items.LoadAsync();
        Logs = new LogWatcher(Maps);
        Shots = new ScreenshotWatcher();
        _ = Maps.RefreshMarkersAsync();
        if (Settings.StartWithWindows)
            StartupRegistration.Apply(true);
        ApplyWatchers();
    }

    private static void SanitizePlacements(AppSettings s)
    {
        s.MainWindowPlacement = SanitizeOne(s.MainWindowPlacement);
        s.OverlayWindowPlacement = SanitizeOne(s.OverlayWindowPlacement);
        s.ItemLensWindowPlacement = SanitizeOne(s.ItemLensWindowPlacement);
    }

    private static WindowPlacement SanitizeOne(WindowPlacement? p)
    {
        p ??= new WindowPlacement();
        if (IsNonFinite(p.Left)) p.Left = null;
        if (IsNonFinite(p.Top)) p.Top = null;
        if (IsNonFinite(p.Width) || p.Width is <= 0) p.Width = null;
        if (IsNonFinite(p.Height) || p.Height is <= 0) p.Height = null;
        if (!IsReasonable(p)) p.Clear();
        return p;
    }

    private static bool IsReasonable(WindowPlacement p)
    {
        if (!p.IsValid) return true;
        return p.Width is >= 100 and <= 8000 &&
               p.Height is >= 80 and <= 8000 &&
               p.Left is > -10000 and < 10000 &&
               p.Top is > -10000 and < 10000;
    }

    private static bool IsNonFinite(double? v) =>
        v is double d && (double.IsNaN(d) || double.IsInfinity(d));

    public static void ApplyWatchers()
    {
        Logs.LogsFolder = Settings.LogsFolder;
        Shots.Folder = Settings.ScreenshotsFolder;
        Shots.DeleteAfterRead = Settings.DeleteAfterRead;
        Shots.KeepLast = Settings.KeepLastScreenshot;
        Logs.Start();
        Shots.Start();
        SettingsStore.Save(Settings);
    }

    public static void ShutdownItemScan()
    {
        ItemScan?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore.Save(Settings);
        Logs.Dispose();
        Shots.Dispose();
        ShutdownItemScan();
        base.OnExit(e);
    }
}
