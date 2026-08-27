using System.Reflection;

namespace Tarkovy.Services;

public static class AssetBootstrap
{
    public static string DirectoryPath => Path.Combine(SettingsStore.AppDataDir, "assets");

    public static string Ensure()
    {
        Directory.CreateDirectory(DirectoryPath);
        var asm = Assembly.GetExecutingAssembly();
        const string prefix = "Tarkovy.Assets.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var file = name[prefix.Length..];
            if (string.IsNullOrWhiteSpace(file)) continue;
            var dest = Path.Combine(DirectoryPath, file);
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var fs = File.Create(dest);
            stream.CopyTo(fs);
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (Directory.Exists(bundled))
        {
            foreach (var file in Directory.EnumerateFiles(bundled))
            {
                var dest = Path.Combine(DirectoryPath, Path.GetFileName(file));
                File.Copy(file, dest, overwrite: true);
            }
        }

        return DirectoryPath;
    }
}
