using System.Windows;
using Tarkovy.Models;
using Tarkovy.Services;

namespace Tarkovy;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    public static MapCatalog Maps { get; } = new();
    public static RaidSession Raid { get; } = new();
    public static LogWatcher Logs { get; private set; } = null!;
    public static ScreenshotWatcher Shots { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Settings = SettingsStore.Load();
        Loc.Apply(Settings.UiLanguage);
        var assets = AssetBootstrap.Ensure();
        Maps.LoadBundled(assets);
        Logs = new LogWatcher(Maps);
        Shots = new ScreenshotWatcher();
        _ = Maps.RefreshMarkersAsync();
        if (Settings.StartWithWindows)
            StartupRegistration.Apply(true);
        ApplyWatchers();
    }

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

    protected override void OnExit(ExitEventArgs e)
    {
        SettingsStore.Save(Settings);
        Logs.Dispose();
        Shots.Dispose();
        base.OnExit(e);
    }
}
