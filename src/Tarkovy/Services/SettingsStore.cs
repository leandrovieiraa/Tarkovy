using System.IO;
using System.Text.Json;

namespace Tarkovy.Services;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string AppDataDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tarkovy");

    public static string SettingsPath => Path.Combine(AppDataDir, "settings.json");

    public static Models.AppSettings Load()
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<Models.AppSettings>(json, JsonOptions) ?? new Models.AppSettings();
            }
        }
        catch
        {
            // keep defaults
        }

        var settings = new Models.AppSettings();
        Save(settings);
        return settings;
    }

    public static void Save(Models.AppSettings settings)
    {
        Directory.CreateDirectory(AppDataDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
