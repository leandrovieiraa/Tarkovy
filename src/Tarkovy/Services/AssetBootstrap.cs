using System.Reflection;

namespace Tarkovy.Services;

public static class AssetBootstrap
{
    private static readonly string[] VersionedDataFiles =
    [
        "quests.json",
        "maps.json",
        "extracts.json",
        "mines.json",
        "spawns.json"
    ];

    public static string DirectoryPath => Path.Combine(SettingsStore.AppDataDir, "assets");

    public static string Ensure()
    {
        Directory.CreateDirectory(DirectoryPath);

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets");
        if (Directory.Exists(bundled))
        {
            CopyTreeIfNewer(bundled, DirectoryPath);
            RefreshVersionedData(bundled, DirectoryPath);
        }
        else
            ExtractEmbeddedResources();

        return DirectoryPath;
    }

    private static void RefreshVersionedData(string bundled, string destRoot)
    {
        var stamp = Path.Combine(destRoot, ".assets-version");
        var version = ProductInfo.AppVersion;
        if (File.Exists(stamp) && string.Equals(File.ReadAllText(stamp).Trim(), version, StringComparison.Ordinal))
            return;

        foreach (var file in VersionedDataFiles)
        {
            var src = Path.Combine(bundled, file);
            if (File.Exists(src))
                ForceCopy(src, Path.Combine(destRoot, file));
        }

        Directory.CreateDirectory(destRoot);
        File.WriteAllText(stamp, version);
    }

    private static void ForceCopy(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
    }

    private static void CopyTreeIfNewer(string sourceRoot, string destRoot)
    {
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(sourceRoot, file);
            CopyFileIfNewer(file, Path.Combine(destRoot, rel));
        }
    }

    private static void CopyFileIfNewer(string source, string dest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        if (File.Exists(dest))
        {
            var srcInfo = new FileInfo(source);
            var dstInfo = new FileInfo(dest);
            if (dstInfo.Length == srcInfo.Length && dstInfo.LastWriteTimeUtc >= srcInfo.LastWriteTimeUtc)
                return;
        }

        File.Copy(source, dest, overwrite: true);
    }

    private static string ResourceNameToRelativePath(string resourceName, string prefix)
    {
        var rel = resourceName[prefix.Length..];
        var parts = rel.Split('.');
        if (parts.Length == 2)
            return rel;

        if (parts.Length > 2)
        {
            var dir = string.Join(Path.DirectorySeparatorChar, parts[..^2]);
            var file = $"{parts[^2]}.{parts[^1]}";
            return Path.Combine(dir, file);
        }

        return rel;
    }

    private static void ExtractEmbeddedResources()
    {
        var asm = Assembly.GetExecutingAssembly();
        const string prefix = "Tarkovy.Assets.";
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal)) continue;

            var rel = ResourceNameToRelativePath(name, prefix);
            if (string.IsNullOrWhiteSpace(rel)) continue;

            var dest = Path.Combine(DirectoryPath, rel);
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null) continue;

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (File.Exists(dest) && new FileInfo(dest).Length == stream.Length)
                continue;

            using var fs = File.Create(dest);
            stream.CopyTo(fs);
        }
    }
}
