using System.Text.Json.Serialization;

namespace MFGUnlockerManager.Models;

public enum InstallMode
{
    StandaloneProxy,
    BinkHooked,
    OptiScalerPlugin
}

public enum GamePlatformSource
{
    Manual,
    Steam,
    Epic,
    Xbox,
    Gog,
    Other
}

public sealed class InstalledGameEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string GameName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public InstallMode Mode { get; set; } = InstallMode.StandaloneProxy;
    public string InstalledFileName { get; set; } = "version.dll";
    public string? HookedBackupFileName { get; set; }
    public string InstalledVersionTag { get; set; } = string.Empty;
    public string InstalledDllSha256 { get; set; } = string.Empty;
    public DateTimeOffset InstalledAt { get; set; } = DateTimeOffset.UtcNow;
    public GamePlatformSource Platform { get; set; } = GamePlatformSource.Manual;

    [JsonIgnore]
    public bool IsInstalledOnDisk { get; set; }

    [JsonIgnore]
    public bool HasUpdateAvailable { get; set; }

    [JsonIgnore]
    public string StatusText { get; set; } = string.Empty;

    [JsonIgnore]
    public bool HasAntiCheatWarning { get; set; }

    [JsonIgnore]
    public bool HasOptiScalerInFolder { get; set; }

    [JsonIgnore]
    public string ModeDisplay => Mode switch
    {
        InstallMode.OptiScalerPlugin => $"OptiScaler Plugin ({InstalledFileName})",
        InstallMode.BinkHooked => $"Bink Hook ({InstalledFileName} -> {HookedBackupFileName})",
        _ => $"Proxy DLL ({InstalledFileName})"
    };
}

public sealed class AppManifest
{
    public string? LatestKnownVersionTag { get; set; }
    public string? ActiveCachedVersionTag { get; set; }
    public string Language { get; set; } = "en-US";
    public string Theme { get; set; } = "Default";
    public bool CheckUpdatesOnStartup { get; set; } = true;
    public string? GitHubToken { get; set; }
    public List<InstalledGameEntry> InstalledGames { get; set; } = [];
    public List<string> CustomLibraryFolders { get; set; } = [];
    public List<CachedReleaseInfo> CachedReleases { get; set; } = [];
}
