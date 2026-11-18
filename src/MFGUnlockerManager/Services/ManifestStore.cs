using System.Text.Json;
using MFGUnlockerManager.Models;

namespace MFGUnlockerManager.Services;

public sealed class ManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public string BaseDataDirectory { get; }
    public string ReleasesCacheDirectory => Path.Combine(BaseDataDirectory, "releases");
    public string ManifestFilePath => Path.Combine(BaseDataDirectory, "manifest.json");
    public string CompatibilityCacheFilePath => Path.Combine(BaseDataDirectory, "compatibility.md");

    public AppManifest Manifest { get; private set; } = new();

    public ManifestStore(string? customBaseDirectory = null)
    {
        BaseDataDirectory = customBaseDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MFGUnlockerManager");

        Directory.CreateDirectory(BaseDataDirectory);
        Directory.CreateDirectory(ReleasesCacheDirectory);
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(ManifestFilePath))
            {
                var json = File.ReadAllText(ManifestFilePath);
                var loaded = JsonSerializer.Deserialize<AppManifest>(json, JsonOptions);
                if (loaded != null)
                {
                    Manifest = loaded;
                }
            }
        }
        catch
        {
            Manifest = new AppManifest();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(BaseDataDirectory);
        var json = JsonSerializer.Serialize(Manifest, JsonOptions);
        File.WriteAllText(ManifestFilePath, json);
    }

    public CachedReleaseInfo? GetCachedRelease(string? tagName = null)
    {
        var targetTag = tagName ?? Manifest.ActiveCachedVersionTag ?? Manifest.LatestKnownVersionTag;
        if (string.IsNullOrWhiteSpace(targetTag))
        {
            return Manifest.CachedReleases
                .Where(r => File.Exists(r.DllPath))
                .OrderByDescending(r => r.DownloadedAt)
                .FirstOrDefault();
        }

        var found = Manifest.CachedReleases.FirstOrDefault(r =>
            r.TagName.Equals(targetTag, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(r.DllPath));

        return found ?? Manifest.CachedReleases
            .Where(r => File.Exists(r.DllPath))
            .OrderByDescending(r => r.DownloadedAt)
            .FirstOrDefault();
    }

    public HashSet<string> GetKnownMfgDllHashes()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // v1.7.1 official SHA-256
            "5a389e6cad23b2b607e1dc224e9e86ee32d6ef46f0557b6c6f8137feadf0aa74"
        };

        foreach (var rel in Manifest.CachedReleases)
        {
            if (!string.IsNullOrWhiteSpace(rel.DllSha256))
                set.Add(rel.DllSha256);
        }

        foreach (var game in Manifest.InstalledGames)
        {
            if (!string.IsNullOrWhiteSpace(game.InstalledDllSha256))
                set.Add(game.InstalledDllSha256);
        }

        return set;
    }

    public string? FindVersionByDllHash(string sha256)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            return null;

        if (sha256.Equals("5a389e6cad23b2b607e1dc224e9e86ee32d6ef46f0557b6c6f8137feadf0aa74", StringComparison.OrdinalIgnoreCase))
            return "v1.7.1";

        var rel = Manifest.CachedReleases.FirstOrDefault(r =>
            r.DllSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase));

        return rel?.TagName;
    }

    public long GetTotalCacheSizeBytes()
    {
        if (!Directory.Exists(ReleasesCacheDirectory))
            return 0;

        return Directory.GetFiles(ReleasesCacheDirectory, "*", SearchOption.AllDirectories)
            .Sum(f => new FileInfo(f).Length);
    }

    public void ClearReleaseCache()
    {
        if (Directory.Exists(ReleasesCacheDirectory))
        {
            Directory.Delete(ReleasesCacheDirectory, recursive: true);
            Directory.CreateDirectory(ReleasesCacheDirectory);
        }
        Manifest.CachedReleases.Clear();
        Manifest.ActiveCachedVersionTag = null;
        Save();
    }
}
