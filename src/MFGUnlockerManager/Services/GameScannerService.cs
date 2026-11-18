using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MFGUnlockerManager.Models;

namespace MFGUnlockerManager.Services;

public sealed class DiscoveredGame
{
    public string GameName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string InstallDirectory => Path.GetDirectoryName(ExecutablePath) ?? string.Empty;
    public GamePlatformSource Platform { get; set; }
    public bool MatchesCompatibilityList { get; set; }
    public bool HasDlss { get; set; }
    public bool HasDlssFrameGen { get; set; }
    public bool HasCrackOrRepackMarker { get; set; }

    public string PlatformDisplay => Platform switch
    {
        GamePlatformSource.Other => HasCrackOrRepackMarker ? "Crack / Repack" : "Standalone Game",
        GamePlatformSource.Gog => "GOG",
        _ => Platform.ToString()
    };

    public string Badge
    {
        get
        {
            var parts = new List<string>();
            if (HasDlssFrameGen) parts.Add("DLSS FG ✓");
            else if (HasDlss) parts.Add("DLSS SR");
            if (MatchesCompatibilityList) parts.Add("★ Verified Compat");
            return string.Join("  ·  ", parts);
        }
    }
}

public sealed class GameMarkers
{
    public bool HasDlss { get; set; }
    public bool HasDlssFrameGen { get; set; }
    public bool HasEngineMarker { get; set; }
    public bool HasCrackOrRepackMarker { get; set; }
    public bool HasAntiCheat { get; set; }
    public bool IsElectronOrCefApp { get; set; }

    public bool LooksLikeValidGame =>
        !HasAntiCheat &&
        !IsElectronOrCefApp &&
        (HasDlss || HasDlssFrameGen || HasEngineMarker || HasCrackOrRepackMarker);
}

public sealed class GameScanOptions
{
    public bool Steam { get; set; } = true;
    public bool Epic { get; set; } = true;
    public bool Xbox { get; set; } = true;
    public bool Gog { get; set; } = true;
    public bool UninstallRegistry { get; set; } = true;
    public bool CommonFolders { get; set; } = true;
    public List<string> CustomFolders { get; set; } = [];
}

public sealed class GameScannerService
{
    private const int MarkerSearchDepth = 8;

    private static readonly HashSet<string> DlssFrameGenFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvngx_dlssg.dll",
        "sl.dlss_g.dll",
        "libxess_fg.dll",
        "amd_fidelityfx_framegeneration_dx12.dll"
    };

    private static readonly HashSet<string> DlssOrUpscalerFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "nvngx_dlss.dll",
        "nvngx_dlssd.dll",
        "nvngx_dlssnr.dll",
        "sl.interposer.dll",
        "sl.dlss.dll",
        "sl.common.dll",
        "libxess.dll",
        "amd_fidelityfx_dx12.dll",
        "amd_fidelityfx_vk.dll",
        "OptiScaler.ini",
        "OptiScaler.dll"
    };

    private static readonly HashSet<string> EngineIndicatorFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnityPlayer.dll",
        "re_chunk_000.pak",
        "GFSDK_Aftermath_Lib.x64.dll",
        "bink2w64.dll",
        "binkw64.dll",
        "amd_ags_x64.dll"
    };

    // Only genuine Steam/crack emulator files (NEVER include unins000.exe or _CommonRedist which match normal desktop apps!)
    private static readonly HashSet<string> CrackOrSteamEmuFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam_api64.dll",
        "steamclient64.dll",
        "steam_emu.ini",
        "CODEX.ini",
        "ALI213.ini",
        "OnlineFix64.dll",
        "OnlineFix.ini",
        "EMPRESS.ini",
        "SmartSteamEmu.ini",
        "cream_api.ini",
        "Goldberg.ini",
        "rune.ini",
        "CPY.ini",
        "3DMGAME.ini",
        "FitGirl-Launcher.exe",
        "coldloader.dll"
    };

    // Anti-cheat files & folders -> immediately disqualify a game
    private static readonly HashSet<string> AntiCheatFileOrDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "EasyAntiCheat",
        "EasyAntiCheat_EOS.exe",
        "EasyAntiCheat_x64.dll",
        "EasyAntiCheat.exe",
        "BattlEye",
        "BEService.exe",
        "BEService_x64.exe",
        "BEClient_x64.dll",
        "Riot Vanguard",
        "vgk.sys",
        "vgc.exe",
        "AntiCheatExpert",
        "ACE-Base.sys",
        "SGuard64.exe",
        "ACE-CSI64.dll",
        "HoYoKProtect.sys",
        "mhyprot2.sys",
        "mhyprot3.sys",
        "EAAntiCheat.GameServiceLauncher.exe",
        "EAAntiCheat.Installer.exe",
        "GameMon.des",
        "x3.xem",
        "xhunter1.sys",
        "randgrid.sys",
        "pbsvc.exe"
    };

    // Esports & Competitive Online games -> excluded per user requirement
    private static readonly string[] EsportsAndAntiCheatGameKeywords =
    [
        "League of Legends", "Liên Minh Huyền Thoại", "LeagueClient",
        "Teamfight Tactics", "Đấu Trường Chân Lý",
        "Valorant", "Riot Games", "Riot Client", "Riot Vanguard",
        "Counter-Strike", "csgo", "cs2", "Dota 2", "dota2", "Deadlock", "Team Fortress 2",
        "Apex Legends", "r5apex", "Fortnite", "Rocket League", "Fall Guys",
        "Overwatch", "Hearthstone", "Heroes of the Storm", "StarCraft",
        "Rainbow Six", "RainbowSix", "Brawlhalla", "For Honor", "XDefiant", "The Finals",
        "PUBG", "TslGame", "Naraka", "Marvel Rivals", "Strinova", "FragPunk",
        "EA SPORTS FC", "FIFA", "eFootball", "NBA 2K",
        "osu!", "Roblox", "CrossFire", "Đột Kích", "Audition", "FC Online",
        "Paladins", "Smite", "War Thunder", "World of Tanks", "World of Warships",
        "Escape from Tarkov", "Arena Breakout", "Delta Force", "Rust", "DayZ", "Dead by Daylight",
        "VRChat", "Among Us", "Battlefield", "RedSec", "Forza Horizon 6",
        "Gears of War: E-Day", "Grand Theft Auto V Enhanced", "GTA5_Enhanced",
        "Zenless Zone Zero", "ZenlessZoneZero", "Genshin Impact", "GenshinImpact", "Honkai", "Star Rail"
    ];

    // Desktop Apps, Launchers, Benchmarks, Emulators & System Utilities -> never scan as games
    private static readonly string[] NonGameAppKeywords =
    [
        "Wallpaper Engine", "wallpaper_engine", "wallpaper32", "wallpaper64",
        "Hydra", "hydralauncher", "SteamTools", "Playnite", "Heroic", "Lutris", "Vortex", "ModOrganizer",
        "Minecraft Launcher", "PrismLauncher", "Prism Launcher", "TLauncher", "CurseForge", "Lunar Client", "Badlion",
        "FurMark", "Heaven Benchmark", "Valley Benchmark", "Superposition", "3DMark", "PCMark", "Cinebench", "Geekbench",
        "CrystalDisk", "HWMonitor", "CPU-Z", "GPU-Z", "MSI Center", "Afterburner", "RivaTuner",
        "Display Driver Uninstaller", "PawnIO", "HWiNFO", "TreeSize", "PowerToys",
        "TikTok LIVE", "OBS Studio", "Streamlabs", "Voicemod", "FxSound", "EqualizerAPO",
        "Discord", "Spotify", "Chrome", "Microsoft Edge", "EdgeWebView", "Zen Browser", "CocCoc", "Cốc Cốc", "Firefox", "Brave", "Opera",
        "Soulseek", "JDownloader", "qBittorrent", "uTorrent", "BitTorrent",
        "Krita", "Blender", "Photoshop", "Premiere", "CapCut", "DaVinci",
        "XAMPP", "WinRAR", "7-Zip", "VLC", "PotPlayer", "LonelyScreen", "AirPlay", "uxplay", "ApowerMirror",
        "Key Presser", "Radmin", "Hamachi", "ZeroTier", "Cloudflare", "Malwarebytes",
        "Ollama", "Visual Studio", "VS Code", "Antigravity", "Python", "Java", "MSYS2", "Docker", "VMware", "VirtualBox",
        "BlueStacks", "LDPlayer", "Nox", "MuMu", "GameLoop",
        "REDlauncher", "HoYoPlay", "CanhCutTeam", "Google Drive", "OneDrive", "GeForce NOW", "Soundtrack"
    ];

    private static readonly HashSet<string> SkippedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Windows", "$Recycle.Bin", "System Volume Information", "AppData", "ProgramData", "node_modules", ".git",
        "Common Files", "drivers", "WindowsApps", "WpSystem", "WindowsPowerShell", "Windows Defender", "Windows NT",
        "Windows Mail", "Windows Media Player", "Windows Photo Viewer", "Windows Sidebar", "Internet Explorer",
        "Reference Assemblies", "dotnet", "MSBuild", "PackageManagement", "ModifiableWindowsApps",
        "SteamLibrary", "steamapps", "Steam", "Epic Games", "XboxGames", "GOG Galaxy", "Uninstall Information",
        "_CommonRedist", "_Redist", "Redist", "DirectX", "APP", "DrZero_Apps", "Riot Games", "HoYoPlay", "SteamTools",
        "xampp", "msys64", "inetpub", "PerfLogs", "GGDRIVECACHE", "HIGHLIGHTS", "usb"
    };

    private static readonly string[] SkippedFolderPrefixes =
    [
        "Microsoft", "NVIDIA", "Intel", "AMD", "Realtek", "Windows Kits", "ASUS", "MSI", "DeepCool", "CPUID"
    ];

    // Only scan dedicated game root folders on drives (do NOT scan C:\Program Files which contains desktop apps!)
    private static readonly string[] CommonGameFolderNames =
    [
        "Games", "GAMES", "Game", "Repack", "Repacks", "Cracked Games", "PC Games"
    ];

    private readonly CompatibilityService _compatibilityService;

    public GameScannerService(CompatibilityService compatibilityService)
    {
        _compatibilityService = compatibilityService;
    }

    public List<DiscoveredGame> ScanAllPlatforms() => ScanAll(new GameScanOptions(), null, CancellationToken.None);

    public Task<List<DiscoveredGame>> ScanAllAsync(GameScanOptions options, IProgress<string>? progress, CancellationToken ct)
        => Task.Run(() => ScanAll(options, progress, ct), ct);

    public List<DiscoveredGame> ScanAll(GameScanOptions options, IProgress<string>? progress, CancellationToken ct)
    {
        var list = new List<DiscoveredGame>();

        if (options.Steam) { progress?.Report("Steam..."); list.AddRange(ScanSteamLibraries()); }
        ct.ThrowIfCancellationRequested();
        if (options.Epic) { progress?.Report("Epic Games..."); list.AddRange(ScanEpicGames()); }
        ct.ThrowIfCancellationRequested();
        if (options.Xbox) { progress?.Report("Xbox / Game Pass..."); list.AddRange(ScanXboxGames()); }
        ct.ThrowIfCancellationRequested();
        if (options.Gog) { progress?.Report("GOG..."); list.AddRange(ScanGogGames()); }
        ct.ThrowIfCancellationRequested();
        if (options.UninstallRegistry) { progress?.Report("Registry (Repack / Installed Games)..."); list.AddRange(ScanUninstallRegistry(ct)); }
        ct.ThrowIfCancellationRequested();

        var roots = new List<string>(options.CustomFolders);
        if (options.CommonFolders)
            roots.AddRange(GetCommonGameFolders());

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            list.AddRange(ScanLibraryFolder(root, progress, ct));
        }

        return MergeAndSort(list);
    }

    public static List<DiscoveredGame> MergeAndSort(IEnumerable<DiscoveredGame> games)
    {
        return games
            .Where(g => !string.IsNullOrWhiteSpace(g.ExecutablePath))
            // Deduplicate by normalized game name or install directory root so the same game doesn't appear twice
            .GroupBy(g => Path.GetFullPath(g.ExecutablePath), StringComparer.OrdinalIgnoreCase)
            .Select(grp => grp.OrderBy(g => g.Platform == GamePlatformSource.Other ? 1 : 0).First())
            .OrderByDescending(g => g.HasDlssFrameGen)
            .ThenByDescending(g => g.MatchesCompatibilityList)
            .ThenByDescending(g => g.HasDlss)
            .ThenBy(g => g.GameName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IEnumerable<string> GetCommonGameFolders()
    {
        foreach (var drive in SafeFixedDrives())
        {
            var root = drive.RootDirectory.FullName;
            foreach (var name in CommonGameFolderNames)
            {
                var p = Path.Combine(root, name);
                if (Directory.Exists(p))
                    yield return p;
            }
        }
    }

    private static IEnumerable<DriveInfo> SafeFixedDrives()
    {
        try
        {
            return DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static bool IsSkippedFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return true;

        var name = Path.GetFileName(folderPath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name))
            return false;

        if (SkippedFolderNames.Contains(name))
            return true;

        if (SkippedFolderPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            return true;

        if (IsNonGameAppOrUtility(name) || IsEsportsOrAntiCheatTitle(name))
            return true;

        return false;
    }

    public static bool IsNonGameAppOrUtility(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
            return false;

        // Reject desktop apps inside AppData (except Temp used by tests), Windows, or system driver directories
        if ((nameOrPath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase) &&
             !nameOrPath.Contains(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase)) ||
            nameOrPath.Contains(@"\Windows\", StringComparison.OrdinalIgnoreCase) ||
            nameOrPath.Contains(@"\Program Files\NVIDIA", StringComparison.OrdinalIgnoreCase) ||
            nameOrPath.Contains(@"\Program Files\Intel", StringComparison.OrdinalIgnoreCase) ||
            nameOrPath.Contains(@"\Program Files (x86)\Intel", StringComparison.OrdinalIgnoreCase) ||
            nameOrPath.Contains(@"\Program Files (x86)\Microsoft", StringComparison.OrdinalIgnoreCase) ||
            nameOrPath.Contains(@"\Program Files\Microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return NonGameAppKeywords.Any(k => nameOrPath.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsEsportsOrAntiCheatTitle(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
            return false;

        return EsportsAndAntiCheatGameKeywords.Any(k => nameOrPath.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Scans a library folder like D:\Games: the folder itself (if it is a game) and every child folder are game candidates.
    /// </summary>
    public List<DiscoveredGame> ScanLibraryFolder(string libraryRoot, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var results = new List<DiscoveredGame>();
        if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot) || IsSkippedFolder(libraryRoot))
            return results;

        var rootCandidate = EvaluateCandidateFolder(libraryRoot, GamePlatformSource.Other, requireTopLevelExe: true);
        if (rootCandidate != null)
        {
            results.Add(rootCandidate);
            return results;
        }

        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(libraryRoot).ToList();
        }
        catch
        {
            return results;
        }

        foreach (var child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (IsSkippedFolder(child))
                continue;

            progress?.Report(child);
            var candidate = EvaluateCandidateFolder(child, GamePlatformSource.Other);
            if (candidate != null)
                results.Add(candidate);
        }

        return results;
    }

    public DiscoveredGame? EvaluateCandidateFolder(
        string gameRoot,
        GamePlatformSource platform,
        string? displayName = null,
        bool requireTopLevelExe = false)
    {
        try
        {
            var folderName = Path.GetFileName(gameRoot.TrimEnd('\\', '/'));
            var checkTitle = displayName ?? folderName;

            if (IsNonGameAppOrUtility(checkTitle) || IsNonGameAppOrUtility(gameRoot) ||
                IsEsportsOrAntiCheatTitle(checkTitle) || IsEsportsOrAntiCheatTitle(gameRoot))
            {
                return null;
            }

            if (requireTopLevelExe && !Directory.EnumerateFiles(gameRoot, "*.exe", SearchOption.TopDirectoryOnly).Any())
                return null;

            var markers = DetectGameMarkers(gameRoot, requireTopLevelExe ? 3 : MarkerSearchDepth);
            if (!markers.LooksLikeValidGame)
                return null;

            var exe = FindBestGameExecutable(gameRoot);
            if (string.IsNullOrEmpty(exe))
                return null;

            if (IsNonGameAppOrUtility(exe) || IsEsportsOrAntiCheatTitle(exe))
                return null;

            // Double-check anti-cheat in the chosen executable's folder as well
            if (CompatibilityService.DetectAntiCheat(Path.GetDirectoryName(exe) ?? gameRoot, checkTitle))
                return null;

            var compat = _compatibilityService.FindMatchingGame(checkTitle)
                      ?? _compatibilityService.FindMatchingGame(folderName)
                      ?? _compatibilityService.FindMatchingGame(exe);

            var finalName = compat?.GameName ?? CleanReleaseGroupSuffix(checkTitle);

            return new DiscoveredGame
            {
                GameName = finalName,
                ExecutablePath = exe,
                Platform = platform,
                MatchesCompatibilityList = compat != null,
                HasDlss = markers.HasDlss,
                HasDlssFrameGen = markers.HasDlssFrameGen,
                HasCrackOrRepackMarker = markers.HasCrackOrRepackMarker
            };
        }
        catch
        {
            return null;
        }
    }

    private static string CleanReleaseGroupSuffix(string rawName)
    {
        return Regex.Replace(rawName, @"(-SteamRIP\.com|-FitGirl|-DODI|-RUNE|-CODEX|-ELAMIGOS|-TENOKE)$", "", RegexOptions.IgnoreCase)
            .Replace('.', ' ')
            .Trim();
    }

    public static GameMarkers DetectGameMarkers(string gameRoot, int maxDepth = MarkerSearchDepth)
    {
        var markers = new GameMarkers();
        if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            return markers;

        // Quick check for Electron desktop apps (resources\app.asar or resources\app)
        if (File.Exists(Path.Combine(gameRoot, "resources", "app.asar")) ||
            Directory.Exists(Path.Combine(gameRoot, "resources", "app")))
        {
            markers.IsElectronOrCefApp = true;
            return markers;
        }

        var opts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = maxDepth,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(gameRoot, "*", opts))
            {
                var name = Path.GetFileName(entry);

                if (AntiCheatFileOrDirNames.Contains(name))
                {
                    markers.HasAntiCheat = true;
                    return markers;
                }

                if (name.Equals("app.asar", StringComparison.OrdinalIgnoreCase))
                {
                    markers.IsElectronOrCefApp = true;
                    return markers;
                }

                if (DlssFrameGenFiles.Contains(name))
                {
                    markers.HasDlssFrameGen = true;
                    markers.HasDlss = true;
                }
                else if (DlssOrUpscalerFiles.Contains(name))
                {
                    markers.HasDlss = true;
                }
                else if (EngineIndicatorFiles.Contains(name) ||
                         name.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase) ||
                         (name.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !entry.Contains(@"\locales\", StringComparison.OrdinalIgnoreCase)) ||
                         name.EndsWith(".utoc", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("data.unity3d", StringComparison.OrdinalIgnoreCase) ||
                         name.Equals("globalgamemanagers", StringComparison.OrdinalIgnoreCase))
                {
                    markers.HasEngineMarker = true;
                }
                else if (CrackOrSteamEmuFiles.Contains(name) ||
                         name.EndsWith(".cdx", StringComparison.OrdinalIgnoreCase) ||
                         (name.Equals("steam_settings", StringComparison.OrdinalIgnoreCase) && Directory.Exists(entry)))
                {
                    markers.HasCrackOrRepackMarker = true;
                }
            }
        }
        catch
        {
        }

        return markers;
    }

    public List<DiscoveredGame> ScanUninstallRegistry(CancellationToken ct = default)
    {
        var results = new List<DiscoveredGame>();
        var sources = new (RegistryKey Hive, string Path)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
            (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall")
        };

        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

        foreach (var (hive, path) in sources)
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key == null)
                    continue;

                foreach (var subName in key.GetSubKeyNames())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        using var sub = key.OpenSubKey(subName);
                        var displayName = sub?.GetValue("DisplayName") as string;
                        var location = (sub?.GetValue("InstallLocation") as string)?.Trim().Trim('"');
                        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(location) || !Directory.Exists(location))
                            continue;

                        if (location.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase) || IsSkippedFolder(location))
                            continue;

                        // Skip generic C:\Program Files desktop apps unless they have DLSS or Steam/crack markers
                        bool inProgramFiles =
                            (!string.IsNullOrEmpty(programFiles) && location.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(programFilesX86) && location.StartsWith(programFilesX86, StringComparison.OrdinalIgnoreCase));

                        if (inProgramFiles &&
                            !location.Contains("Steam", StringComparison.OrdinalIgnoreCase) &&
                            !location.Contains("Epic", StringComparison.OrdinalIgnoreCase) &&
                            !location.Contains("GOG", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (Path.GetPathRoot(location)?.TrimEnd('\\').Equals(location.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) == true)
                            continue;

                        var candidate = EvaluateCandidateFolder(location, GamePlatformSource.Other, displayName);
                        if (candidate != null)
                            results.Add(candidate);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
            }
        }

        return results;
    }

    public List<DiscoveredGame> ScanGogGames()
    {
        var results = new List<DiscoveredGame>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
            if (key == null)
                return results;

            foreach (var id in key.GetSubKeyNames())
            {
                try
                {
                    using var sub = key.OpenSubKey(id);
                    var name = sub?.GetValue("gameName") as string;
                    var path = sub?.GetValue("path") as string;
                    if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                        continue;

                    var candidate = EvaluateCandidateFolder(path, GamePlatformSource.Gog, name);
                    if (candidate != null)
                        results.Add(candidate);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return results;
    }

    public List<DiscoveredGame> ScanSteamLibraries()
    {
        var results = new List<DiscoveredGame>();
        try
        {
            string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                             ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string
                             ?? @"C:\Program Files (x86)\Steam";

            if (string.IsNullOrWhiteSpace(steamPath) || !Directory.Exists(steamPath))
                return results;

            var libraryDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.Combine(steamPath.Replace('/', '\\'), "steamapps")
            };

            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdfPath))
            {
                var vdfText = File.ReadAllText(vdfPath);
                foreach (Match m in Regex.Matches(vdfText, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase))
                {
                    var libRoot = m.Groups[1].Value.Replace(@"\\", @"\");
                    var steamApps = Path.Combine(libRoot, "steamapps");
                    if (Directory.Exists(steamApps))
                        libraryDirs.Add(steamApps);
                }
            }

            foreach (var steamAppsDir in libraryDirs)
            {
                foreach (var acfFile in Directory.GetFiles(steamAppsDir, "appmanifest_*.acf"))
                {
                    try
                    {
                        var acfText = File.ReadAllText(acfFile);
                        var nameMatch = Regex.Match(acfText, @"""name""\s+""([^""]+)""");
                        var dirMatch = Regex.Match(acfText, @"""installdir""\s+""([^""]+)""");
                        if (!nameMatch.Success || !dirMatch.Success)
                            continue;

                        var gameName = nameMatch.Groups[1].Value;
                        if (gameName.Contains("Steamworks", StringComparison.OrdinalIgnoreCase) ||
                            gameName.Contains("Proton", StringComparison.OrdinalIgnoreCase) ||
                            gameName.Contains("Soundtrack", StringComparison.OrdinalIgnoreCase) ||
                            IsNonGameAppOrUtility(gameName) ||
                            IsEsportsOrAntiCheatTitle(gameName))
                        {
                            continue;
                        }

                        var gameRoot = Path.Combine(steamAppsDir, "common", dirMatch.Groups[1].Value);
                        if (!Directory.Exists(gameRoot))
                            continue;

                        var candidate = EvaluateCandidateFolder(gameRoot, GamePlatformSource.Steam, gameName);
                        if (candidate != null)
                            results.Add(candidate);
                    }
                    catch
                    {
                    }
                }
            }
        }
        catch
        {
        }

        return results;
    }

    public List<DiscoveredGame> ScanEpicGames()
    {
        var results = new List<DiscoveredGame>();
        var manifestsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");

        if (!Directory.Exists(manifestsDir))
            return results;

        try
        {
            foreach (var itemFile in Directory.GetFiles(manifestsDir, "*.item"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(itemFile));
                    var root = doc.RootElement;
                    var displayName = root.TryGetProperty("DisplayName", out var dn) ? dn.GetString() : null;
                    var installLoc = root.TryGetProperty("InstallLocation", out var il) ? il.GetString() : null;
                    if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(installLoc) || !Directory.Exists(installLoc))
                        continue;

                    var candidate = EvaluateCandidateFolder(installLoc, GamePlatformSource.Epic, displayName);
                    if (candidate != null)
                        results.Add(candidate);
                }
                catch
                {
                }
            }
        }
        catch
        {
        }

        return results;
    }

    public List<DiscoveredGame> ScanXboxGames()
    {
        var results = new List<DiscoveredGame>();
        foreach (var drive in SafeFixedDrives())
        {
            var xboxRoot = Path.Combine(drive.RootDirectory.FullName, "XboxGames");
            if (!Directory.Exists(xboxRoot))
                continue;

            try
            {
                foreach (var gameDir in Directory.GetDirectories(xboxRoot))
                {
                    var gameName = Path.GetFileName(gameDir);
                    if (gameName.Equals("GameSave", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var contentDir = Path.Combine(gameDir, "Content");
                    var searchDir = Directory.Exists(contentDir) ? contentDir : gameDir;
                    var candidate = EvaluateCandidateFolder(searchDir, GamePlatformSource.Xbox, gameName);
                    if (candidate != null)
                        results.Add(candidate);
                }
            }
            catch
            {
            }
        }

        return results;
    }

    public static string? FindBestGameExecutable(string gameRootDir)
    {
        if (string.IsNullOrWhiteSpace(gameRootDir) || !Directory.Exists(gameRootDir))
            return null;

        var searchOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MaxRecursionDepth = 5,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };

        try
        {
            var allExes = Directory.EnumerateFiles(gameRootDir, "*.exe", searchOpts)
                .Where(IsCandidateGameExecutable)
                .ToList();

            if (allExes.Count == 0)
                return null;

            return allExes
                .OrderByDescending(ScoreExecutableCandidate)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsCandidateGameExecutable(string exePath)
    {
        var fileName = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        // Ignore redistributable / thirdparty / installer subdirectories
        string[] ignoredPathSegments =
        [
            @"\_Redist\", @"\_CommonRedist\", @"\Redist\", @"\DirectX\",
            @"\Engine\Binaries\ThirdParty\", @"\Engine\Extras\", @"\Installers\"
        ];
        if (ignoredPathSegments.Any(seg => exePath.Contains(seg, StringComparison.OrdinalIgnoreCase)))
            return false;

        string[] ignoredExeKeywords =
        [
            "unins", "crash", "setup", "redist", "vcredist", "visualcpp", "dotnet", "dxsetup", "dxwebsetup",
            "oalinst", "physx", "unitycrashhandler", "cefsubprocess", "epicwebhelper", "easyanticheat",
            "beservice", "fitgirl", "rapidcrc", "quicksfv", "checkenv", "8za", "7za", "launcher",
            "updater", "report", "helper", "config", "settings", "benchmark", "ue4prereq", "ueprereq",
            "steamerrorreporter", "activation"
        ];

        return !ignoredExeKeywords.Any(k => fileName.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    private static double ScoreExecutableCandidate(string exePath)
    {
        double score = 0;
        var fileName = Path.GetFileName(exePath);
        var dir = Path.GetDirectoryName(exePath) ?? string.Empty;

        if (fileName.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase))
            score += 1000;

        if (dir.EndsWith(@"Binaries\Win64", StringComparison.OrdinalIgnoreCase) ||
            dir.EndsWith(@"bin\x64_dx12", StringComparison.OrdinalIgnoreCase) ||
            dir.EndsWith(@"bin\x64", StringComparison.OrdinalIgnoreCase))
        {
            score += 500;
        }

        // Check if OptiScaler, DLSS, or Steam API sits beside this executable
        if (File.Exists(Path.Combine(dir, "OptiScaler.ini")) ||
            File.Exists(Path.Combine(dir, "nvngx_dlssg.dll")) ||
            File.Exists(Path.Combine(dir, "nvngx_dlss.dll")) ||
            File.Exists(Path.Combine(dir, "sl.interposer.dll")))
        {
            score += 600;
        }

        if (File.Exists(Path.Combine(dir, "steam_api64.dll")) ||
            File.Exists(Path.Combine(dir, "bink2w64.dll")))
        {
            score += 300;
        }

        try
        {
            var sizeMb = new FileInfo(exePath).Length / (1024.0 * 1024.0);
            score += Math.Min(sizeMb, 500);
        }
        catch
        {
        }

        return score;
    }
}
