using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;
using MFGUnlockerManager.Models;

namespace MFGUnlockerManager.Services;

public sealed class InstallService
{
    private readonly ManifestStore _manifestStore;
    private readonly CompatibilityService _compatibilityService;

    public static readonly IReadOnlyList<string> LegacyModFiles =
    [
        "RTXMFG.dll",
        "RTX40MFGCore.dll",
        "RTX40MFG.asi",
        "RTX40MFG-UI.addon64"
    ];

    public InstallService(ManifestStore manifestStore, CompatibilityService compatibilityService)
    {
        _manifestStore = manifestStore;
        _compatibilityService = compatibilityService;
    }

    public static bool IsGameProcessRunning(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return false;

        var procName = Path.GetFileNameWithoutExtension(executablePath);
        if (string.IsNullOrWhiteSpace(procName))
            return false;

        try
        {
            var processes = Process.GetProcessesByName(procName);
            return processes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public InstalledGameEntry InstallOrUpdateGame(
        string gameName,
        string executablePath,
        InstallMode mode,
        string proxyFileName,
        CachedReleaseInfo release,
        GamePlatformSource platform = GamePlatformSource.Manual)
    {
        if (!File.Exists(release.DllPath))
            throw new FileNotFoundException("Cached MFGUnlocker.dll not found. Please download a release first.", release.DllPath);

        var gameDir = Path.GetDirectoryName(executablePath)
            ?? throw new ArgumentException("Invalid executable path.", nameof(executablePath));

        if (!Directory.Exists(gameDir))
            throw new DirectoryNotFoundException($"Game directory does not exist: {gameDir}");

        if (IsGameProcessRunning(executablePath))
            throw new InvalidOperationException($"Game '{Path.GetFileName(executablePath)}' is currently running. Please close the game before installing or updating.");

        BackupLegacyFilesIfPresent(gameDir);

        var existingEntry = _manifestStore.Manifest.InstalledGames.FirstOrDefault(g =>
            g.InstallDirectory.Equals(gameDir, StringComparison.OrdinalIgnoreCase) ||
            g.ExecutablePath.Equals(executablePath, StringComparison.OrdinalIgnoreCase));

        // If switching proxy filename or mode on an existing installation, clean up previous installed file first
        if (existingEntry != null)
        {
            RemoveInstalledFilesOnly(existingEntry, gameDir);
        }

        string installedRelativePath;
        string? hookedBackupName = null;
        var knownHashes = _manifestStore.GetKnownMfgDllHashes();
        knownHashes.Add(release.DllSha256);

        if (mode == InstallMode.OptiScalerPlugin)
        {
            var pluginDir = Path.Combine(gameDir, "OptiScaler", "plugins");
            Directory.CreateDirectory(pluginDir);
            installedRelativePath = Path.Combine("OptiScaler", "plugins", "MFGUnlocker.asi");
            var targetAsiPath = Path.Combine(gameDir, installedRelativePath);
            File.Copy(release.DllPath, targetAsiPath, overwrite: true);

            EnsureOptiScalerIniLoadsAsiPlugins(gameDir);
        }
        else if (mode == InstallMode.BinkHooked ||
                 proxyFileName.Equals("binkw64.dll", StringComparison.OrdinalIgnoreCase) ||
                 proxyFileName.Equals("bink2w64.dll", StringComparison.OrdinalIgnoreCase))
        {
            var binkPath = Path.Combine(gameDir, proxyFileName);
            hookedBackupName = proxyFileName.Equals("binkw64.dll", StringComparison.OrdinalIgnoreCase)
                ? "binkw64Hooked.dll"
                : "bink2w64Hooked.dll";
            var hookedPath = Path.Combine(gameDir, hookedBackupName);

            if (File.Exists(binkPath))
            {
                var currentHash = CompatibilityService.ComputeFileSha256(binkPath);
                if (!knownHashes.Contains(currentHash))
                {
                    // Original Bink DLL -> back it up to *Hooked.dll
                    if (!File.Exists(hookedPath))
                    {
                        File.Copy(binkPath, hookedPath, overwrite: false);
                    }
                    mode = InstallMode.BinkHooked;
                }
            }

            if (File.Exists(hookedPath))
            {
                mode = InstallMode.BinkHooked;
            }
            else
            {
                hookedBackupName = null;
                mode = InstallMode.StandaloneProxy;
            }

            File.Copy(release.DllPath, binkPath, overwrite: true);
            installedRelativePath = proxyFileName;
        }
        else
        {
            var targetDllPath = Path.Combine(gameDir, proxyFileName);
            if (File.Exists(targetDllPath))
            {
                var existingHash = CompatibilityService.ComputeFileSha256(targetDllPath);
                if (!knownHashes.Contains(existingHash))
                {
                    throw new InvalidOperationException(
                        $"File '{proxyFileName}' already exists in the game folder and belongs to the game or another mod. Choose another proxy filename to avoid overwriting it.");
                }
            }

            File.Copy(release.DllPath, targetDllPath, overwrite: true);
            installedRelativePath = proxyFileName;
        }

        if (existingEntry == null)
        {
            existingEntry = new InstalledGameEntry
            {
                GameName = gameName,
                ExecutablePath = executablePath,
                InstallDirectory = gameDir,
                Platform = platform
            };
            _manifestStore.Manifest.InstalledGames.Add(existingEntry);
        }

        existingEntry.GameName = string.IsNullOrWhiteSpace(gameName) ? existingEntry.GameName : gameName;
        existingEntry.ExecutablePath = executablePath;
        existingEntry.InstallDirectory = gameDir;
        existingEntry.Mode = mode;
        existingEntry.InstalledFileName = installedRelativePath;
        existingEntry.HookedBackupFileName = hookedBackupName;
        existingEntry.InstalledVersionTag = release.TagName;
        existingEntry.InstalledDllSha256 = release.DllSha256;
        existingEntry.InstalledAt = DateTimeOffset.UtcNow;

        RefreshEntryStatus(existingEntry);
        _manifestStore.Save();

        return existingEntry;
    }

    public void UninstallGame(InstalledGameEntry entry, bool removeSettingsAndLogs = false, bool removeFromList = false)
    {
        if (IsGameProcessRunning(entry.ExecutablePath))
            throw new InvalidOperationException($"Game '{Path.GetFileName(entry.ExecutablePath)}' is currently running. Please close the game before uninstalling.");

        RemoveInstalledFilesOnly(entry, entry.InstallDirectory);

        if (removeSettingsAndLogs && Directory.Exists(entry.InstallDirectory))
        {
            var settingsJson = Path.Combine(entry.InstallDirectory, "RTXMFG-Universal.json");
            if (File.Exists(settingsJson))
                File.Delete(settingsJson);

            var uiIni = Path.Combine(entry.InstallDirectory, "RTX40MFG-UI.ini");
            if (File.Exists(uiIni))
                File.Delete(uiIni);

            foreach (var log in GetGameLogFiles(entry.InstallDirectory))
            {
                try { File.Delete(log); } catch { }
            }
        }

        if (removeFromList)
        {
            _manifestStore.Manifest.InstalledGames.Remove(entry);
        }
        else
        {
            entry.InstalledVersionTag = string.Empty;
            entry.InstalledDllSha256 = string.Empty;
            RefreshEntryStatus(entry);
        }

        _manifestStore.Save();
    }

    private static void RemoveInstalledFilesOnly(InstalledGameEntry entry, string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return;

        var targetPath = Path.Combine(gameDir, entry.InstalledFileName);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        // Restore Bink *Hooked.dll if applicable
        if (entry.Mode == InstallMode.BinkHooked && !string.IsNullOrWhiteSpace(entry.HookedBackupFileName))
        {
            var hookedPath = Path.Combine(gameDir, entry.HookedBackupFileName);
            if (File.Exists(hookedPath))
            {
                File.Move(hookedPath, targetPath, overwrite: true);
            }
        }
    }

    private static void BackupLegacyFilesIfPresent(string gameDir)
    {
        string? backupDir = null;
        foreach (var legacyFile in LegacyModFiles)
        {
            var fullPath = Path.Combine(gameDir, legacyFile);
            if (File.Exists(fullPath))
            {
                backupDir ??= Path.Combine(gameDir, "_MFG_Legacy_Backup");
                Directory.CreateDirectory(backupDir);
                var dest = Path.Combine(backupDir, legacyFile);
                File.Move(fullPath, dest, overwrite: true);
            }
        }
    }

    public static void EnsureOptiScalerIniLoadsAsiPlugins(string gameDir)
    {
        var iniPath = Path.Combine(gameDir, "OptiScaler.ini");
        if (!File.Exists(iniPath))
            return;

        var text = File.ReadAllText(iniPath);
        if (Regex.IsMatch(text, @"^\s*LoadAsiPlugins\s*=\s*true", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            return;

        if (Regex.IsMatch(text, @"^\s*LoadAsiPlugins\s*=\s*(false|auto|0)", RegexOptions.Multiline | RegexOptions.IgnoreCase))
        {
            text = Regex.Replace(
                text,
                @"^(\s*LoadAsiPlugins\s*=\s*)(false|auto|0)",
                "${1}true",
                RegexOptions.Multiline | RegexOptions.IgnoreCase);
            File.WriteAllText(iniPath, text);
            return;
        }

        if (text.Contains("[Plugins]", StringComparison.OrdinalIgnoreCase))
        {
            text = Regex.Replace(
                text,
                @"(\[Plugins\]\s*\r?\n)",
                "$1LoadAsiPlugins = true\r\n",
                RegexOptions.IgnoreCase);
        }
        else
        {
            text += "\r\n[Plugins]\r\nLoadAsiPlugins = true\r\n";
        }

        File.WriteAllText(iniPath, text);
    }

    public void RefreshAllGamesStatus()
    {
        foreach (var entry in _manifestStore.Manifest.InstalledGames)
        {
            RefreshEntryStatus(entry);
        }
    }

    public void RefreshEntryStatus(InstalledGameEntry entry)
    {
        var isVi = _manifestStore.Manifest.Language.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
        entry.HasAntiCheatWarning = CompatibilityService.DetectAntiCheat(entry.InstallDirectory, entry.GameName);
        entry.HasOptiScalerInFolder = CompatibilityService.DetectOptiScaler(entry.InstallDirectory);

        var targetPath = Path.Combine(entry.InstallDirectory, entry.InstalledFileName);
        if (!File.Exists(targetPath))
        {
            // Try to auto-detect if user installed under another supported proxy name in that folder
            var detected = DetectExistingInstallationInFolder(entry.InstallDirectory);
            if (detected != null)
            {
                entry.Mode = detected.Value.Mode;
                entry.InstalledFileName = detected.Value.RelativePath;
                entry.InstalledDllSha256 = detected.Value.Sha256;
                var matchedVer = _manifestStore.FindVersionByDllHash(detected.Value.Sha256);
                if (!string.IsNullOrEmpty(matchedVer))
                    entry.InstalledVersionTag = matchedVer;
                targetPath = Path.Combine(entry.InstallDirectory, entry.InstalledFileName);
            }
        }

        if (!File.Exists(targetPath))
        {
            entry.IsInstalledOnDisk = false;
            entry.HasUpdateAvailable = false;
            entry.StatusText = isVi ? "Chưa cài đặt" : "Not installed";
            return;
        }

        entry.IsInstalledOnDisk = true;
        try
        {
            var diskHash = CompatibilityService.ComputeFileSha256(targetPath);
            var matchedVer = _manifestStore.FindVersionByDllHash(diskHash);
            if (!string.IsNullOrEmpty(matchedVer))
            {
                entry.InstalledVersionTag = matchedVer;
                entry.InstalledDllSha256 = diskHash;
            }
        }
        catch
        {
            // Ignore read errors
        }

        var latestTag = _manifestStore.Manifest.LatestKnownVersionTag ?? _manifestStore.Manifest.ActiveCachedVersionTag;
        var latestCached = _manifestStore.GetCachedRelease(latestTag);

        bool updateByTag = !string.IsNullOrWhiteSpace(latestTag) &&
                           !string.IsNullOrWhiteSpace(entry.InstalledVersionTag) &&
                           GitHubReleaseService.CompareVersionTags(entry.InstalledVersionTag, latestTag) < 0;

        bool updateByHash = latestCached != null &&
                            !string.IsNullOrWhiteSpace(entry.InstalledDllSha256) &&
                            !entry.InstalledDllSha256.Equals(latestCached.DllSha256, StringComparison.OrdinalIgnoreCase) &&
                            GitHubReleaseService.CompareVersionTags(entry.InstalledVersionTag, latestCached.TagName) <= 0;

        entry.HasUpdateAvailable = updateByTag || updateByHash;
        if (entry.HasUpdateAvailable)
        {
            entry.StatusText = isVi
                ? $"Có bản cập nhật ({entry.InstalledVersionTag} → {latestTag})"
                : $"Update available ({entry.InstalledVersionTag} → {latestTag})";
        }
        else
        {
            var verLabel = string.IsNullOrWhiteSpace(entry.InstalledVersionTag) ? "Custom/Installed" : entry.InstalledVersionTag;
            entry.StatusText = isVi ? $"Đã cài ({verLabel})" : $"Up to date ({verLabel})";
        }
    }

    public (InstallMode Mode, string RelativePath, string Sha256)? DetectExistingInstallationInFolder(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return null;

        var asiRel = Path.Combine("OptiScaler", "plugins", "MFGUnlocker.asi");
        var asiFull = Path.Combine(gameDir, asiRel);
        if (File.Exists(asiFull))
        {
            return (InstallMode.OptiScalerPlugin, asiRel, CompatibilityService.ComputeFileSha256(asiFull));
        }

        var knownHashes = _manifestStore.GetKnownMfgDllHashes();
        foreach (var dll in CompatibilityService.SupportedProxyFileNames)
        {
            var full = Path.Combine(gameDir, dll);
            if (!File.Exists(full))
                continue;

            try
            {
                var hash = CompatibilityService.ComputeFileSha256(full);
                if (knownHashes.Contains(hash))
                {
                    bool isBink = (dll.Equals("binkw64.dll", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(gameDir, "binkw64Hooked.dll"))) ||
                                  (dll.Equals("bink2w64.dll", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(gameDir, "bink2w64Hooked.dll")));
                    return (isBink ? InstallMode.BinkHooked : InstallMode.StandaloneProxy, dll, hash);
                }
            }
            catch
            {
            }
        }

        return null;
    }

    public static List<string> GetGameLogFiles(string gameDir)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(gameDir) && Directory.Exists(gameDir))
        {
            list.AddRange(Directory.GetFiles(gameDir, "RTXMFG*.log"));
            var statusJson = Path.Combine(gameDir, "RTXMFG-Universal.status.json");
            if (File.Exists(statusJson))
                list.Add(statusJson);
        }

        // Also check %TEMP% for RTXMFG-*.log
        try
        {
            var tempLogs = Directory.GetFiles(Path.GetTempPath(), "RTXMFG*.log");
            list.AddRange(tempLogs);
        }
        catch
        {
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string ExportDiagnosticsZip(InstalledGameEntry entry, string destinationZipPath)
    {
        if (File.Exists(destinationZipPath))
            File.Delete(destinationZipPath);

        using var zip = ZipFile.Open(destinationZipPath, ZipArchiveMode.Create);
        var filesToInclude = new List<string>();
        filesToInclude.AddRange(GetGameLogFiles(entry.InstallDirectory));

        var configJson = Path.Combine(entry.InstallDirectory, "RTXMFG-Universal.json");
        if (File.Exists(configJson))
            filesToInclude.Add(configJson);

        var uiIni = Path.Combine(entry.InstallDirectory, "RTX40MFG-UI.ini");
        if (File.Exists(uiIni))
            filesToInclude.Add(uiIni);

        foreach (var file in filesToInclude.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(file))
            {
                zip.CreateEntryFromFile(file, Path.GetFileName(file));
            }
        }

        var summaryEntry = zip.CreateEntry("diagnostic-summary.txt");
        using (var writer = new StreamWriter(summaryEntry.Open()))
        {
            writer.WriteLine($"Game: {entry.GameName}");
            writer.WriteLine($"Executable: {entry.ExecutablePath}");
            writer.WriteLine($"Mode: {entry.ModeDisplay}");
            writer.WriteLine($"Installed Version: {entry.InstalledVersionTag}");
            writer.WriteLine($"DLL SHA256: {entry.InstalledDllSha256}");
            writer.WriteLine($"Exported At: {DateTimeOffset.Now:O}");
        }

        return destinationZipPath;
    }
}
