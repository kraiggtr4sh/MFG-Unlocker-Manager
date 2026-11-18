using System.Security.Cryptography;
using System.Text.RegularExpressions;
using MFGUnlockerManager.Models;

namespace MFGUnlockerManager.Services;

public sealed class CompatibilityService
{
    public static readonly IReadOnlyList<string> SupportedProxyFileNames =
    [
        "version.dll",
        "winmm.dll",
        "dinput8.dll",
        "dxgi.dll",
        "d3d12.dll",
        "d3d11.dll",
        "d3d10.dll",
        "d3d9.dll",
        "dsound.dll",
        "wininet.dll",
        "winhttp.dll",
        "bink2w64.dll",
        "binkw64.dll",
        "xinput1_4.dll",
        "xinput1_3.dll",
        "xinput1_2.dll",
        "xinput1_1.dll",
        "xinput9_1_0.dll",
        "xinputuap.dll"
    ];

    public static readonly IReadOnlyDictionary<string, string> CommonlyUsedByWarnings =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["version.dll"] = "Cyber Engine Tweaks (CP2077), Ultimate ASI Loader",
            ["winmm.dll"] = "RED4ext (CP2077), Crimson Desert mod loader, Phantom Blade Zero",
            ["dinput8.dll"] = "REFramework (Monster Hunter Wilds, Dragon's Dogma 2, RE Engine)",
            ["dxgi.dll"] = "ReShade, OptiScaler, Special K",
            ["dsound.dll"] = "FF7RebirthFix, ASI loaders"
        };

    public static readonly IReadOnlyList<string> KnownAntiCheatGames =
    [
        "Battlefield 6",
        "RedSec",
        "Forza Horizon 6",
        "Gears of War: E-Day",
        "Grand Theft Auto V Enhanced",
        "GTA5_Enhanced",
        "Zenless Zone Zero",
        "ZenlessZoneZero"
    ];

    private List<GameCompatibilityEntry> _entries = [];

    public IReadOnlyList<GameCompatibilityEntry> Entries => _entries;

    public CompatibilityService()
    {
        _entries = ParseCompatibilityMarkdown(BundledCompatibilityMarkdown);
    }

    public void LoadFromMarkdown(string markdown)
    {
        var parsed = ParseCompatibilityMarkdown(markdown);
        if (parsed.Count > 0)
        {
            _entries = parsed;
        }
    }

    public static List<GameCompatibilityEntry> ParseCompatibilityMarkdown(string markdown)
    {
        var results = new List<GameCompatibilityEntry>();
        if (string.IsNullOrWhiteSpace(markdown))
            return results;

        var lines = markdown.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        bool inTable = false;

        // Also capture "Through OptiScaler" section
        var optiScalerSectionText = string.Empty;
        int optiIdx = markdown.IndexOf("### Through OptiScaler", StringComparison.OrdinalIgnoreCase);
        if (optiIdx >= 0)
        {
            int nextHeader = markdown.IndexOf("\n## ", optiIdx, StringComparison.OrdinalIgnoreCase);
            optiScalerSectionText = nextHeader > optiIdx
                ? markdown[optiIdx..nextHeader]
                : markdown[optiIdx..];
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("| Game", StringComparison.OrdinalIgnoreCase))
            {
                inTable = true;
                continue;
            }

            if (inTable)
            {
                if (!line.StartsWith('|'))
                {
                    inTable = false;
                    continue;
                }

                if (line.Contains("---"))
                    continue;

                var cells = line.Split('|')
                    .Skip(1)
                    .SkipLast(1)
                    .Select(c => c.Trim())
                    .ToArray();

                if (cells.Length < 4)
                    continue;

                var gameName = cells[0].Trim();
                if (string.IsNullOrEmpty(gameName))
                    continue;

                var entry = new GameCompatibilityEntry
                {
                    GameName = gameName,
                    RawUseColumn = StripMarkdownLinks(cells[1]),
                    RawAlsoColumn = StripMarkdownLinks(cells[2]),
                    RawDontUseColumn = StripMarkdownLinks(cells[3]),
                    RecommendedUse = ExtractDllNames(cells[1]),
                    AlsoLoaded = ExtractDllNames(cells[2]),
                    DoNotUse = ExtractDllNames(cells[3])
                };

                var cleanTitle = Regex.Replace(gameName, @"\s*\(.*?\)\s*", "").Trim();
                if (!string.IsNullOrEmpty(optiScalerSectionText) &&
                    optiScalerSectionText.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase))
                {
                    entry.SupportsOptiScaler = true;
                }

                results.Add(entry);
            }
        }

        return results;
    }

    private static string StripMarkdownLinks(string text)
    {
        return Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1").Replace("`", "");
    }

    public static List<string> ExtractDllNames(string cellText)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(cellText))
            return list;

        var matches = Regex.Matches(cellText, @"([a-zA-Z0-9_]+\.dll)", RegexOptions.IgnoreCase);
        foreach (Match m in matches)
        {
            var dll = m.Groups[1].Value.ToLowerInvariant();
            if (!list.Contains(dll, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(dll);
            }
        }
        return list;
    }

    public GameCompatibilityEntry? FindMatchingGame(string gameNameOrExePath)
    {
        if (string.IsNullOrWhiteSpace(gameNameOrExePath))
            return null;

        var search = Path.GetFileNameWithoutExtension(gameNameOrExePath)
            .Replace("-Win64-Shipping", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", " ")
            .Replace("-", " ")
            .Trim();

        // Check exact or substring match (require >= 5 chars for substring match to avoid short false positives)
        foreach (var entry in _entries)
        {
            var cleanEntry = Regex.Replace(entry.GameName, @"\s*\(.*?\)\s*", "").Trim();
            if (cleanEntry.Equals(search, StringComparison.OrdinalIgnoreCase) ||
                (search.Length >= 5 && cleanEntry.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (cleanEntry.Length >= 5 && search.Contains(cleanEntry, StringComparison.OrdinalIgnoreCase)))
            {
                return entry;
            }
        }

        // Common executable aliases
        var exeAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cyberpunk2077"] = "Cyberpunk 2077",
            ["witcher3"] = "The Witcher 3",
            ["b1"] = "Black Myth: Wukong",
            ["PBZ"] = "Phantom Blade Zero",
            ["AlanWake2"] = "Alan Wake 2",
            ["MonsterHunterWilds"] = "Monster Hunter Wilds",
            ["DD2"] = "Dragon's Dogma 2",
            ["Stalker2"] = "S.T.A.L.K.E.R. 2",
            ["FlightSimulator"] = "Microsoft Flight Simulator 2020",
            ["FlightSimulator2024"] = "Microsoft Flight Simulator 2024",
            ["ff7rebirth"] = "Final Fantasy VII Rebirth",
            ["tlou-ii"] = "The Last of Us Part II",
            ["HogwartsLegacy"] = "Hogwarts Legacy",
            ["Spider-Man2"] = "Marvel's Spider-Man 2",
            ["TheGreatCircle"] = "Indiana Jones and the Great Circle",
            ["Client-Win64-Shipping"] = "Wuthering Waves"
        };

        foreach (var kv in exeAliases)
        {
            if (search.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            {
                return _entries.FirstOrDefault(e => e.GameName.Contains(kv.Value, StringComparison.OrdinalIgnoreCase));
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if the user selected an Unreal Engine root stub launcher and locates the real Binaries\Win64 executable if present.
    /// </summary>
    public static string? SuggestRealExecutableIfLauncher(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return null;

        var dir = Path.GetDirectoryName(exePath);
        if (string.IsNullOrEmpty(dir))
            return null;

        var fileName = Path.GetFileName(exePath);
        // Check if Unreal Engine Binaries\Win64 exists inside a child folder
        try
        {
            var shippingExes = Directory.GetFiles(dir, "*-Win64-Shipping.exe", SearchOption.AllDirectories);
            if (shippingExes.Length > 0 && !fileName.EndsWith("-Win64-Shipping.exe", StringComparison.OrdinalIgnoreCase))
            {
                return shippingExes[0];
            }

            // Witcher 3 bin\x64_dx12 check
            var tw3Dx12 = Path.Combine(dir, "bin", "x64_dx12", "witcher3.exe");
            if (File.Exists(tw3Dx12) && !exePath.Equals(tw3Dx12, StringComparison.OrdinalIgnoreCase))
            {
                return tw3Dx12;
            }

            // Cyberpunk bin\x64\Cyberpunk2077.exe check
            var cp2077 = Path.Combine(dir, "bin", "x64", "Cyberpunk2077.exe");
            if (File.Exists(cp2077) && !exePath.Equals(cp2077, StringComparison.OrdinalIgnoreCase))
            {
                return cp2077;
            }
        }
        catch
        {
            // Ignore permission errors in deep directories
        }

        if (fileName.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
            fileName.Contains("crash", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty; // Indicates generic launcher warning
        }

        return null;
    }

    public static bool DetectOptiScaler(string gameDirectory)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return false;

        return File.Exists(Path.Combine(gameDirectory, "OptiScaler.ini")) ||
               File.Exists(Path.Combine(gameDirectory, "OptiScaler.dll")) ||
               Directory.Exists(Path.Combine(gameDirectory, "OptiScaler", "plugins"));
    }

    public static bool DetectAntiCheat(string gameDirectory, string? gameName = null)
    {
        if (!string.IsNullOrWhiteSpace(gameName))
        {
            foreach (var acGame in KnownAntiCheatGames)
            {
                if (gameName.Contains(acGame, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return false;

        var checkDirs = new List<string> { gameDirectory };
        var parent = Directory.GetParent(gameDirectory)?.FullName;
        if (!string.IsNullOrEmpty(parent))
            checkDirs.Add(parent);
        var grandParent = parent != null ? Directory.GetParent(parent)?.FullName : null;
        if (!string.IsNullOrEmpty(grandParent))
            checkDirs.Add(grandParent);

        foreach (var d in checkDirs)
        {
            if (Directory.Exists(Path.Combine(d, "EasyAntiCheat")) ||
                Directory.Exists(Path.Combine(d, "BattlEye")) ||
                File.Exists(Path.Combine(d, "EasyAntiCheat_EOS.exe")) ||
                File.Exists(Path.Combine(d, "BEService.exe")))
            {
                return true;
            }
        }

        return false;
    }

    public List<ProxyCandidate> EvaluateCandidatesForFolder(
        string gameDirectory,
        GameCompatibilityEntry? compatEntry,
        ISet<string>? knownMfgDllHashes = null,
        bool isVietnamese = true)
    {
        var candidates = new List<ProxyCandidate>();
        foreach (var dllName in SupportedProxyFileNames)
        {
            bool isBink = dllName.Equals("binkw64.dll", StringComparison.OrdinalIgnoreCase) ||
                          dllName.Equals("bink2w64.dll", StringComparison.OrdinalIgnoreCase);

            var fullPath = Path.Combine(gameDirectory, dllName);
            bool existsOnDisk = File.Exists(fullPath);
            bool isExistingMfgUnlocker = false;

            if (existsOnDisk && knownMfgDllHashes != null && knownMfgDllHashes.Count > 0)
            {
                try
                {
                    var hash = ComputeFileSha256(fullPath);
                    isExistingMfgUnlocker = knownMfgDllHashes.Contains(hash);
                }
                catch
                {
                    // Ignore read lock
                }
            }

            bool isRecommended = compatEntry?.RecommendedUse.Contains(dllName, StringComparer.OrdinalIgnoreCase) == true;
            bool isAlsoLoaded = compatEntry?.AlsoLoaded.Contains(dllName, StringComparer.OrdinalIgnoreCase) == true;
            bool isDoNotUse = compatEntry?.DoNotUse.Contains(dllName, StringComparer.OrdinalIgnoreCase) == true;

            ProxyCandidateState state;
            string badge;
            var notes = new List<string>();
            bool selectable = true;

            if (isDoNotUse)
            {
                state = ProxyCandidateState.DoNotUse;
                badge = isVietnamese ? "Không dùng" : "Don't use";
                if (!string.IsNullOrEmpty(compatEntry?.RawDontUseColumn))
                    notes.Add(compatEntry.RawDontUseColumn);
            }
            else if (isBink && existsOnDisk && !isExistingMfgUnlocker)
            {
                state = ProxyCandidateState.BinkHookRoute;
                badge = isVietnamese ? "Bink Hook (Tự sao lưu *Hooked.dll)" : "Bink Route (Auto-renames *Hooked.dll)";
                notes.Add(isVietnamese
                    ? "Giữ DLL Bink gốc dưới tên *Hooked.dll và cài MFG Unlocker thế chỗ"
                    : "Backs up original Bink DLL to *Hooked.dll and installs MFG Unlocker");
            }
            else if (existsOnDisk && !isExistingMfgUnlocker)
            {
                state = ProxyCandidateState.OccupiedByOtherFile;
                badge = isVietnamese ? "Đã tồn tại file khác" : "Occupied by existing file";
                selectable = false;
                notes.Add(isVietnamese
                    ? "Thư mục game đã có sẵn file này (của game hoặc mod khác). Không ghi đè!"
                    : "Another DLL with this name already exists in the folder. Do not overwrite.");
            }
            else if (isRecommended)
            {
                state = ProxyCandidateState.Recommended;
                badge = isVietnamese ? "Khuyên dùng ★" : "Recommended ★";
                if (!string.IsNullOrEmpty(compatEntry?.RawUseColumn))
                    notes.Add(compatEntry.RawUseColumn);
            }
            else if (isAlsoLoaded)
            {
                state = ProxyCandidateState.AlsoLoaded;
                badge = isVietnamese ? "Tương thích phụ" : "Also works";
                if (!string.IsNullOrEmpty(compatEntry?.RawAlsoColumn))
                    notes.Add(compatEntry.RawAlsoColumn);
            }
            else
            {
                state = ProxyCandidateState.Available;
                badge = isVietnamese ? "Khả dụng" : "Available";
            }

            if (CommonlyUsedByWarnings.TryGetValue(dllName, out var conflictMod))
            {
                notes.Add(isVietnamese
                    ? $"Thường dùng bởi: {conflictMod}"
                    : $"Often used by: {conflictMod}");
            }

            candidates.Add(new ProxyCandidate
            {
                FileName = dllName,
                State = state,
                BadgeText = badge,
                Description = string.Join(" · ", notes),
                IsSelectable = selectable,
                IsBink = isBink
            });
        }

        return candidates
            .OrderBy(c => c.State switch
            {
                ProxyCandidateState.Recommended => 0,
                ProxyCandidateState.AlsoLoaded => 1,
                ProxyCandidateState.BinkHookRoute => 2,
                ProxyCandidateState.Available => 3,
                ProxyCandidateState.DoNotUse => 4,
                ProxyCandidateState.OccupiedByOtherFile => 5,
                _ => 6
            })
            .ToList();
    }

    public static string ComputeFileSha256(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public const string BundledCompatibilityMarkdown = """
| Game | Use | Also loaded | Don't use |
|---|---|---|---|
| Ace Combat 8 | `version.dll` | | |
| AION 2 | `d3d12.dll` | `dxgi.dll` | |
| Alan Wake 2 | `winmm.dll` | `dinput8.dll` | `version.dll` (Epic) |
| Assassin's Creed IV Black Flag Resynced | `version.dll` | `dinput8.dll` | |
| Black Myth: Wukong | `version.dll` | `dsound.dll`, `dxgi.dll` | |
| Call of Duty: Modern Warfare 4 | `dxgi.dll` | | |
| Control Resonant | `version.dll` | `winmm.dll`, `bink2w64.dll` | |
| Crimson Desert | `d3d12.dll` | | `version.dll`, `dinput8.dll` (not loaded); `winmm.dll` (game's mod loader) |
| Cyberpunk 2077 | `xinput9_1_0.dll` | `dxgi.dll`, `d3d12.dll` | `version.dll` (CET), `winmm.dll` (RED4ext), `dinput8.dll` (not loaded) |
| DOOM: The Dark Ages (Vulkan) | `winmm.dll` | | |
| Dragon's Dogma 2 | `winmm.dll` | | |
| Dying Light 2 | `winmm.dll` | `version.dll` | |
| Dying Light: The Beast | `winmm.dll` | | |
| Enshrouded (Vulkan) | `winmm.dll` | | |
| Farming Simulator 25 | `dinput8.dll` | `version.dll` | |
| Final Fantasy VII Rebirth | `version.dll` | `dxgi.dll` | `dsound.dll` if FF7RebirthFix uses it |
| Gothic 1 Remake | `version.dll` | `winmm.dll` | |
| Hogwarts Legacy | `version.dll` | | |
| Indiana Jones and the Great Circle (Vulkan) | `dinput8.dll` | | |
| Marvel's Spider-Man 2 | `version.dll` | | |
| Microsoft Flight Simulator 2020 | `version.dll` (Steam), `dinput8.dll` (Store) | | |
| Microsoft Flight Simulator 2024 | `version.dll` | `dinput8.dll`, `d3d12.dll` | |
| Monster Hunter Wilds | `version.dll` | `winmm.dll` | `dinput8.dll` (REFramework) |
| Onimusha: Way of the Sword | `winmm.dll` | | `version.dll` (crash) |
| Phantom Blade Zero | `version.dll` | `dxgi.dll` | `winmm.dll` (the game's own) |
| Planet Zoo 2 | `winmm.dll` | | `dxgi.dll` (breaks HDR) |
| Pragmata | `version.dll` | | |
| Resident Evil Requiem | `version.dll` | `dxgi.dll` | `dinput8.dll` (REFramework) |
| S.T.A.L.K.E.R. 2 | `version.dll` | `d3d12.dll` | `dxgi.dll` (not loaded) |
| Silent Hill: Townfall | `version.dll` | | |
| Star Wars: Galactic Racer | `version.dll` | `dxgi.dll` | |
| Subnautica 2 | `version.dll` | | |
| The Blood of Dawnwalker | `version.dll` | | |
| The First Descendant | `dxgi.dll` | | |
| The Last of Us Part II | `dinput8.dll` | | |
| The Witcher 3 | `winmm.dll` | `version.dll`, `d3d11.dll` | `dxgi.dll` if ReShade uses it |
| Where Winds Meet | `winmm.dll` | | |
| Wuthering Waves | `version.dll` | `dxgi.dll` | `dinput8.dll`, `winmm.dll`, `d3d12.dll` (no menu on V1.3) |

### Through OptiScaler
007 First Light, Arknights: Endfield, Clair Obscur: Expedition 33, Crimson Desert, Cyberpunk 2077, DOOM: The Dark Ages, Dying Light 2, Enshrouded, Forza Motorsport, GTA: The Trilogy, Hitman World of Assassination, Marvel's Spider-Man Remastered, Neverness to Everness, No Man's Sky, No Rest for the Wicked, Red Dead Redemption 2, Resident Evil Requiem, Silent Hill: Townfall, Subnautica 2, Tainted Grail: The Fall of Avalon, The Ascent, The Last of Us Part II, The Witcher 3, X4: Foundations.
""";
}
