using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MFGUnlockerManager.Models;
using MFGUnlockerManager.Services;
using Wpf.Ui.Appearance;

namespace MFGUnlockerManager.ViewModels;

public sealed record HotkeyOption(int VkCode, string DisplayName);
public sealed record OptiScalerUiOption(string Value, string DisplayName);

public sealed partial class MainViewModel : ObservableObject
{
    private readonly ManifestStore _manifestStore;
    private readonly CompatibilityService _compatibilityService;
    private readonly GitHubReleaseService _releaseService;
    private readonly InstallService _installService;
    private readonly GameScannerService _scannerService;

    public LocalizationService Loc => LocalizationService.Instance;

    [ObservableProperty]
    private int _selectedNavIndex;

    // Status / InfoBar
    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private string _statusTitle = "MFG Unlocker Manager";

    [ObservableProperty]
    private bool _isStatusOpen = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _downloadProgress;

    [ObservableProperty]
    private string _downloadStatusText = string.Empty;

    // Home / Releases state
    [ObservableProperty]
    private GitHubRelease? _latestRelease;

    [ObservableProperty]
    private CachedReleaseInfo? _activeCachedRelease;

    [ObservableProperty]
    private int _outdatedGamesCount;

    public ObservableCollection<GitHubRelease> AllReleases { get; } = [];
    public ObservableCollection<InstalledGameEntry> InstalledGames { get; } = [];
    public ObservableCollection<DiscoveredGame> DiscoveredGames { get; } = [];
    public ObservableCollection<GameCompatibilityEntry> FilteredCompatibilityEntries { get; } = [];

    [ObservableProperty]
    private string _compatibilitySearchText = string.Empty;

    // Game scanning state
    private CancellationTokenSource? _scanCts;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _scanProgressText = string.Empty;

    [ObservableProperty]
    private bool _scanSteam = true;

    [ObservableProperty]
    private bool _scanEpic = true;

    [ObservableProperty]
    private bool _scanXbox = true;

    [ObservableProperty]
    private bool _scanGog = true;

    [ObservableProperty]
    private bool _scanOtherGames = true;

    public ObservableCollection<string> CustomLibraryFolders { get; } = [];

    // Install Wizard State
    [ObservableProperty]
    private bool _isInstallWizardVisible;

    [ObservableProperty]
    private string _wizardGameName = string.Empty;

    [ObservableProperty]
    private string _wizardExecutablePath = string.Empty;

    [ObservableProperty]
    private string? _wizardSuggestedRealExePath;

    [ObservableProperty]
    private bool _wizardHasAntiCheatWarning;

    [ObservableProperty]
    private bool _wizardOptiScalerDetected;

    [ObservableProperty]
    private string _wizardMatchedCompatInfo = string.Empty;

    [ObservableProperty]
    private InstallMode _wizardSelectedMode = InstallMode.StandaloneProxy;

    [ObservableProperty]
    private ProxyCandidate? _wizardSelectedCandidate;

    public ObservableCollection<ProxyCandidate> WizardProxyCandidates { get; } = [];

    // Selected Game Detail (Config & Logs)
    [ObservableProperty]
    private InstalledGameEntry? _selectedGame;

    [ObservableProperty]
    private bool _isGameDetailVisible;

    [ObservableProperty]
    private HotkeyOption _selectedHotkeyOption;

    [ObservableProperty]
    private OptiScalerUiOption _selectedOptiScalerUiOption;

    [ObservableProperty]
    private bool _configGpuNameOverride;

    [ObservableProperty]
    private bool _configRtx30FlipMetering;

    [ObservableProperty]
    private bool _configEnableRtxHair = true;

    [ObservableProperty]
    private string _configRawUniversalJson = "{\n}";

    public ObservableCollection<string> SelectedGameLogs { get; } = [];

    [ObservableProperty]
    private string? _selectedLogFilePath;

    [ObservableProperty]
    private string _selectedLogContent = string.Empty;

    // Settings state
    [ObservableProperty]
    private bool _settingsIsVietnamese = false;

    [ObservableProperty]
    private bool _settingsIsDarkTheme = true;

    [ObservableProperty]
    private bool _settingsCheckUpdatesOnStartup = true;

    [ObservableProperty]
    private string _settingsGitHubToken = string.Empty;

    [ObservableProperty]
    private string _cacheFolderSummary = string.Empty;

    public IReadOnlyList<HotkeyOption> HotkeyOptions { get; } =
    [
        new(8, "Backspace (Default - VK 8)"),
        new(45, "Insert (VK 45)"),
        new(36, "Home (VK 36)"),
        new(35, "End (VK 35)"),
        new(46, "Delete (VK 46)"),
        new(118, "F7 (VK 118)"),
        new(119, "F8 (VK 119)"),
        new(120, "F9 (VK 120)"),
        new(121, "F10 (VK 121)")
    ];

    public IReadOnlyList<OptiScalerUiOption> OptiScalerUiOptions { get; } =
    [
        new("", "Default (Integrate into OptiScaler tab)"),
        new("shared", "shared (Keep MFG Unlocker standalone menu)"),
        new("optiscaler", "optiscaler (Hide MFG Unlocker menu completely)")
    ];

    public MainViewModel()
    {
        _manifestStore = new ManifestStore();
        _compatibilityService = new CompatibilityService();
        _releaseService = new GitHubReleaseService(_manifestStore);
        _installService = new InstallService(_manifestStore, _compatibilityService);
        _scannerService = new GameScannerService(_compatibilityService);

        _selectedHotkeyOption = HotkeyOptions[0];
        _selectedOptiScalerUiOption = OptiScalerUiOptions[0];

        // Load cached compatibility.md if present
        if (File.Exists(_manifestStore.CompatibilityCacheFilePath))
        {
            try
            {
                _compatibilityService.LoadFromMarkdown(File.ReadAllText(_manifestStore.CompatibilityCacheFilePath));
            }
            catch
            {
            }
        }

        // Default to English UI
        _manifestStore.Manifest.Language = "en-US";
        SettingsIsVietnamese = false;
        Loc.SetLanguage("en-US");
        SettingsIsDarkTheme = !_manifestStore.Manifest.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase);
        SettingsCheckUpdatesOnStartup = _manifestStore.Manifest.CheckUpdatesOnStartup;
        SettingsGitHubToken = _manifestStore.Manifest.GitHubToken ?? string.Empty;

        foreach (var folder in _manifestStore.Manifest.CustomLibraryFolders)
        {
            CustomLibraryFolders.Add(folder);
        }

        RefreshLocalState();
        FilterCompatibilityList();
    }

    public async Task InitializeAsync()
    {
        if (SettingsCheckUpdatesOnStartup)
        {
            await CheckForUpdatesAsync();
        }
    }

    partial void OnCompatibilitySearchTextChanged(string value)
    {
        FilterCompatibilityList();
    }

    partial void OnWizardExecutablePathChanged(string value)
    {
        EvaluateWizardExecutable(value);
    }

    partial void OnSelectedLogFilePathChanged(string? value)
    {
        LoadSelectedLogFileContent(value);
    }

    partial void OnSettingsIsVietnameseChanged(bool value)
    {
        var code = value ? "vi-VN" : "en-US";
        _manifestStore.Manifest.Language = code;
        _manifestStore.Save();
        Loc.SetLanguage(code);
        RefreshLocalState();
        if (!string.IsNullOrWhiteSpace(WizardExecutablePath))
        {
            EvaluateWizardExecutable(WizardExecutablePath);
        }
    }

    partial void OnSettingsIsDarkThemeChanged(bool value)
    {
        _manifestStore.Manifest.Theme = value ? "Dark" : "Light";
        _manifestStore.Save();
        App.ApplyWin11BlueAccent(value ? ApplicationTheme.Dark : ApplicationTheme.Light);
    }

    private void FilterCompatibilityList()
    {
        FilteredCompatibilityEntries.Clear();
        var q = CompatibilitySearchText?.Trim() ?? string.Empty;
        foreach (var item in _compatibilityService.Entries)
        {
            if (string.IsNullOrEmpty(q) ||
                item.GameName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.RawUseColumn.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                item.RawAlsoColumn.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                FilteredCompatibilityEntries.Add(item);
            }
        }
    }

    [RelayCommand]
    public void RefreshLocalState()
    {
        _installService.RefreshAllGamesStatus();

        InstalledGames.Clear();
        foreach (var g in _manifestStore.Manifest.InstalledGames)
        {
            InstalledGames.Add(g);
        }

        ActiveCachedRelease = _manifestStore.GetCachedRelease();
        OutdatedGamesCount = InstalledGames.Count(g => g.HasUpdateAvailable);

        var sizeMb = _manifestStore.GetTotalCacheSizeBytes() / (1024.0 * 1024.0);
        CacheFolderSummary = $"{_manifestStore.ReleasesCacheDirectory} ({sizeMb:F2} MB)";
    }

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsBusy)
            return;

        try
        {
            IsBusy = true;
            SetBanner(
                Loc.IsVietnamese ? "Đang kiểm tra..." : "Checking...",
                Loc.IsVietnamese ? "Đang kết nối GitHub API (dashdogy/MFG-Unlocker)..." : "Fetching releases from dashdogy/MFG-Unlocker...");

            var releasesTask = _releaseService.FetchAllReleasesAsync();
            var compatTask = _releaseService.FetchLatestCompatibilityMarkdownAsync();

            await Task.WhenAll(releasesTask, compatTask);

            var releases = await releasesTask;
            AllReleases.Clear();
            foreach (var r in releases)
            {
                AllReleases.Add(r);
            }

            LatestRelease = releases.FirstOrDefault();

            var compatMd = await compatTask;
            if (!string.IsNullOrWhiteSpace(compatMd))
            {
                _compatibilityService.LoadFromMarkdown(compatMd);
                FilterCompatibilityList();
            }

            RefreshLocalState();

            if (LatestRelease != null)
            {
                if (ActiveCachedRelease == null ||
                    GitHubReleaseService.CompareVersionTags(ActiveCachedRelease.TagName, LatestRelease.TagName) < 0)
                {
                    SetBanner(
                        Loc.IsVietnamese ? $"Có phiên bản mới {LatestRelease.TagName}!" : $"New release {LatestRelease.TagName} available!",
                        Loc.IsVietnamese
                            ? $"Bản {LatestRelease.Name} ({LatestRelease.FormattedDate}) đã phát hành. Nhấn 'Tải & Xác thực SHA-256' để tải về máy."
                            : $"{LatestRelease.Name} ({LatestRelease.FormattedDate}) is ready to download.");
                }
                else if (OutdatedGamesCount > 0)
                {
                    SetBanner(
                        Loc.IsVietnamese ? $"Có {OutdatedGamesCount} game cần cập nhật" : $"{OutdatedGamesCount} game(s) have an update",
                        Loc.IsVietnamese
                            ? $"Bạn đã tải sẵn {ActiveCachedRelease.TagName}. Nhấn 'Cập nhật tất cả game' để nâng cấp DLL."
                            : $"Cached {ActiveCachedRelease.TagName} is ready. Click 'Update All Games' to upgrade.");
                }
                else
                {
                    SetBanner(
                        Loc.IsVietnamese ? "Đã ở phiên bản mới nhất" : "Up to date",
                        Loc.IsVietnamese
                            ? $"Phiên bản mới nhất trên GitHub là {LatestRelease.TagName} (SHA-256 đã xác thực)."
                            : $"Latest GitHub release is {LatestRelease.TagName} (SHA-256 verified).");
                }
            }
        }
        catch (Exception ex)
        {
            SetBanner(
                Loc.IsVietnamese ? "Không thể kiểm tra bản mới" : "Update check failed",
                ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task DownloadLatestReleaseAsync()
    {
        if (LatestRelease == null)
        {
            await CheckForUpdatesAsync();
            if (LatestRelease == null)
                return;
        }

        await DownloadSpecificReleaseAsync(LatestRelease);
    }

    [RelayCommand]
    public async Task DownloadSpecificReleaseAsync(GitHubRelease? release)
    {
        if (release == null || IsBusy)
            return;

        try
        {
            IsBusy = true;
            DownloadProgress = 0;
            var progress = new Progress<(double Percent, string Status)>(p =>
            {
                DownloadProgress = p.Percent;
                DownloadStatusText = p.Status;
            });

            var cached = await _releaseService.DownloadAndVerifyReleaseAsync(release, progress);
            RefreshLocalState();

            SetBanner(
                Loc.IsVietnamese ? $"Đã tải & xác thực {cached.TagName}" : $"Downloaded & verified {cached.TagName}",
                Loc.IsVietnamese
                    ? $"MFGUnlocker.dll (SHA-256: {cached.DllSha256[..16]}...) đã sẵn sàng để cài đặt hoặc cập nhật cho game."
                    : $"MFGUnlocker.dll (SHA-256: {cached.DllSha256[..16]}...) is verified and ready.");
        }
        catch (Exception ex)
        {
            SetBanner(
                Loc.IsVietnamese ? "Lỗi tải hoặc xác thực SHA-256" : "Download / SHA-256 verification error",
                ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task UpdateAllOutdatedGamesAsync()
    {
        if (ActiveCachedRelease == null)
        {
            await DownloadLatestReleaseAsync();
            if (ActiveCachedRelease == null)
                return;
        }

        int updated = 0;
        var errors = new List<string>();
        foreach (var game in InstalledGames.Where(g => g.HasUpdateAvailable || !g.IsInstalledOnDisk).ToList())
        {
            try
            {
                _installService.InstallOrUpdateGame(
                    game.GameName,
                    game.ExecutablePath,
                    game.Mode,
                    Path.GetFileName(game.InstalledFileName),
                    ActiveCachedRelease,
                    game.Platform);
                updated++;
            }
            catch (Exception ex)
            {
                errors.Add($"{game.GameName}: {ex.Message}");
            }
        }

        RefreshLocalState();
        if (errors.Count == 0)
        {
            SetBanner(
                Loc.IsVietnamese ? "Cập nhật hoàn tất" : "Bulk update complete",
                Loc.IsVietnamese
                    ? $"Đã cập nhật {updated} game lên phiên bản {ActiveCachedRelease.TagName}."
                    : $"Updated {updated} game(s) to {ActiveCachedRelease.TagName}.");
        }
        else
        {
            SetBanner(
                Loc.IsVietnamese ? $"Đã cập nhật {updated} game, {errors.Count} lỗi" : $"Updated {updated}, {errors.Count} failed",
                string.Join(" | ", errors));
        }
    }

    [RelayCommand]
    public void OpenNewInstallWizard()
    {
        SelectedNavIndex = 1; // Switch to Games page
        IsInstallWizardVisible = true;
        IsGameDetailVisible = false;

        if (string.IsNullOrWhiteSpace(WizardExecutablePath))
        {
            BrowseGameExecutable();
        }
    }

    [RelayCommand]
    public void CloseInstallWizard()
    {
        IsInstallWizardVisible = false;
    }

    [RelayCommand]
    public void BrowseGameExecutable()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.IsVietnamese
                ? "Chọn file thực thi (.exe) thật của Game (không chọn Launcher)"
                : "Select the actual Game Executable (.exe), not the Launcher",
            Filter = "Game Executable (*.exe)|*.exe"
        };

        if (dlg.ShowDialog() == true)
        {
            SelectedNavIndex = 1;
            IsInstallWizardVisible = true;
            IsGameDetailVisible = false;
            WizardExecutablePath = dlg.FileName;
        }
    }

    public void HandleDroppedFileOrFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        SelectedNavIndex = 1;
        IsInstallWizardVisible = true;
        IsGameDetailVisible = false;

        if (Directory.Exists(path))
        {
            // A folder with no exe of its own but many game subfolders = a game library -> add to scan list
            bool hasTopLevelExe = Directory.EnumerateFiles(path, "*.exe", SearchOption.TopDirectoryOnly).Any();
            bool looksLikeSingleGame = new[] { "Engine", "Binaries", "bin", "Content" }
                .Any(d => Directory.Exists(Path.Combine(path, d)));
            if (!hasTopLevelExe && !looksLikeSingleGame && Directory.EnumerateDirectories(path).Count() > 1)
            {
                IsInstallWizardVisible = false;
                AddCustomLibraryFolderPath(path);
                _ = ScanInstalledPlatformGamesAsync();
                return;
            }

            var bestExe = GameScannerService.FindBestGameExecutable(path);
            if (!string.IsNullOrEmpty(bestExe))
            {
                WizardExecutablePath = bestExe;
            }
        }
        else if (File.Exists(path) && path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            WizardExecutablePath = path;
        }
    }

    [RelayCommand]
    public void ApplySuggestedRealExecutable()
    {
        if (!string.IsNullOrWhiteSpace(WizardSuggestedRealExePath) && File.Exists(WizardSuggestedRealExePath))
        {
            WizardExecutablePath = WizardSuggestedRealExePath;
        }
    }

    private void EvaluateWizardExecutable(string exePath)
    {
        WizardProxyCandidates.Clear();
        WizardSuggestedRealExePath = null;
        WizardHasAntiCheatWarning = false;
        WizardOptiScalerDetected = false;
        WizardMatchedCompatInfo = string.Empty;

        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return;

        var gameDir = Path.GetDirectoryName(exePath) ?? string.Empty;
        var suggested = CompatibilityService.SuggestRealExecutableIfLauncher(exePath);
        if (!string.IsNullOrEmpty(suggested) && !suggested.Equals(exePath, StringComparison.OrdinalIgnoreCase))
        {
            WizardSuggestedRealExePath = suggested;
        }

        var compat = _compatibilityService.FindMatchingGame(exePath)
                  ?? _compatibilityService.FindMatchingGame(Path.GetFileName(gameDir));

        if (compat != null)
        {
            WizardGameName = compat.GameName;
            WizardMatchedCompatInfo = Loc.IsVietnamese
                ? $"★ Đã khớp danh sách tương thích: {compat.GameName} — Khuyên dùng: {compat.RawUseColumn}" +
                  (string.IsNullOrEmpty(compat.RawDontUseColumn) ? "" : $" · Tránh dùng: {compat.RawDontUseColumn}")
                : $"★ Matched Compatibility Table: {compat.GameName} — Use: {compat.RawUseColumn}" +
                  (string.IsNullOrEmpty(compat.RawDontUseColumn) ? "" : $" · Don't use: {compat.RawDontUseColumn}");
        }
        else
        {
            WizardGameName = Path.GetFileNameWithoutExtension(exePath)
                .Replace("-Win64-Shipping", "", StringComparison.OrdinalIgnoreCase);
            WizardMatchedCompatInfo = Loc.IsVietnamese
                ? "Chưa có báo cáo riêng cho game này. Hãy thử version.dll hoặc winmm.dll trước."
                : "Not listed in compatibility table yet. Try version.dll or winmm.dll first.";
        }

        WizardHasAntiCheatWarning = CompatibilityService.DetectAntiCheat(gameDir, WizardGameName);
        WizardOptiScalerDetected = CompatibilityService.DetectOptiScaler(gameDir);

        if (WizardOptiScalerDetected)
        {
            WizardSelectedMode = InstallMode.OptiScalerPlugin;
        }
        else
        {
            WizardSelectedMode = InstallMode.StandaloneProxy;
        }

        var candidates = _compatibilityService.EvaluateCandidatesForFolder(
            gameDir,
            compat,
            _manifestStore.GetKnownMfgDllHashes(),
            Loc.IsVietnamese);

        foreach (var c in candidates)
        {
            WizardProxyCandidates.Add(c);
        }

        WizardSelectedCandidate = WizardProxyCandidates.FirstOrDefault(c => c.IsSelectable);
    }

    [RelayCommand]
    public void SelectDiscoveredGameForInstall(DiscoveredGame? game)
    {
        if (game == null)
            return;

        IsInstallWizardVisible = true;
        IsGameDetailVisible = false;
        WizardExecutablePath = game.ExecutablePath;
        WizardGameName = game.GameName;
    }

    [RelayCommand]
    public async Task ConfirmWizardInstallAsync()
    {
        if (string.IsNullOrWhiteSpace(WizardExecutablePath) || !File.Exists(WizardExecutablePath))
        {
            SetBanner(
                Loc.IsVietnamese ? "Chưa chọn file .exe" : "No executable selected",
                Loc.IsVietnamese ? "Vui lòng chọn file .exe của game trước khi cài đặt." : "Please select a valid game executable first.");
            return;
        }

        if (ActiveCachedRelease == null)
        {
            await DownloadLatestReleaseAsync();
            if (ActiveCachedRelease == null)
                return;
        }

        try
        {
            var proxyName = WizardSelectedCandidate?.FileName ?? "version.dll";
            var mode = WizardSelectedMode;
            if (mode != InstallMode.OptiScalerPlugin &&
                WizardSelectedCandidate?.State == ProxyCandidateState.BinkHookRoute)
            {
                mode = InstallMode.BinkHooked;
            }

            var entry = _installService.InstallOrUpdateGame(
                WizardGameName,
                WizardExecutablePath,
                mode,
                proxyName,
                ActiveCachedRelease);

            IsInstallWizardVisible = false;
            RefreshLocalState();

            SetBanner(
                Loc.IsVietnamese ? $"Đã cài MFG Unlocker ({ActiveCachedRelease.TagName}) cho {entry.GameName}!" : $"Installed MFG Unlocker ({ActiveCachedRelease.TagName}) to {entry.GameName}!",
                Loc.IsVietnamese
                    ? $"Đã cài dưới dạng {entry.ModeDisplay}. Vào game, bật Frame Generation và nhấn phím Backspace để mở Menu!"
                    : $"Installed as {entry.ModeDisplay}. Launch the game, enable Frame Generation and press Backspace to open the menu!");
        }
        catch (Exception ex)
        {
            SetBanner(
                Loc.IsVietnamese ? "Cài đặt thất bại" : "Installation failed",
                ex.Message);
        }
    }

    [RelayCommand]
    public async Task ScanInstalledPlatformGamesAsync()
    {
        if (IsScanning)
            return;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanProgressText = Loc.IsVietnamese ? "Đang bắt đầu quét..." : "Starting scan...";

        var options = new GameScanOptions
        {
            Steam = ScanSteam,
            Epic = ScanEpic,
            Xbox = ScanXbox,
            Gog = ScanGog,
            UninstallRegistry = ScanOtherGames,
            CommonFolders = ScanOtherGames,
            CustomFolders = ScanOtherGames ? CustomLibraryFolders.ToList() : []
        };

        var progress = new Progress<string>(p =>
            ScanProgressText = (Loc.IsVietnamese ? "Đang quét: " : "Scanning: ") + p);

        try
        {
            var found = await _scannerService.ScanAllAsync(options, progress, _scanCts.Token);
            DiscoveredGames.Clear();
            foreach (var g in found)
            {
                DiscoveredGames.Add(g);
            }

            var fgCount = found.Count(g => g.HasDlssFrameGen);
            SetBanner(
                Loc.IsVietnamese ? $"Đã quét thấy {found.Count} game ({fgCount} game có DLSS FG)" : $"Found {found.Count} game(s) ({fgCount} with DLSS FG)",
                Loc.IsVietnamese
                    ? "Game có nhãn 'DLSS FG ✓' nhiều khả năng dùng được MFG Unlocker. Nhấn 'Chọn cài đặt' để cài."
                    : "Games tagged 'DLSS FG ✓' are most likely to work with MFG Unlocker. Click 'Configure' to install.");
        }
        catch (OperationCanceledException)
        {
            SetBanner(
                Loc.IsVietnamese ? "Đã hủy quét" : "Scan cancelled",
                Loc.IsVietnamese ? "Quá trình quét game đã bị hủy." : "The game scan was cancelled.");
        }
        catch (Exception ex)
        {
            SetBanner(Loc.IsVietnamese ? "Lỗi khi quét game" : "Scan error", ex.Message);
        }
        finally
        {
            IsScanning = false;
            ScanProgressText = string.Empty;
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }

    [RelayCommand]
    public void CancelScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    public void AddCustomLibraryFolder()
    {
        var dlg = new OpenFolderDialog
        {
            Title = Loc.IsVietnamese
                ? "Chọn thư mục chứa game (VD: D:\\Games)"
                : "Select a folder containing games (e.g. D:\\Games)",
            Multiselect = true
        };

        if (dlg.ShowDialog() == true)
        {
            foreach (var folder in dlg.FolderNames)
            {
                AddCustomLibraryFolderPath(folder);
            }
        }
    }

    public void AddCustomLibraryFolderPath(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        if (CustomLibraryFolders.Any(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase)))
            return;

        CustomLibraryFolders.Add(folder);
        _manifestStore.Manifest.CustomLibraryFolders = CustomLibraryFolders.ToList();
        _manifestStore.Save();
        SetBanner(
            Loc.IsVietnamese ? "Đã thêm thư mục game" : "Game folder added",
            folder);
    }

    [RelayCommand]
    public void RemoveCustomLibraryFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return;

        CustomLibraryFolders.Remove(folder);
        _manifestStore.Manifest.CustomLibraryFolders = CustomLibraryFolders.ToList();
        _manifestStore.Save();
    }

    [RelayCommand]
    public async Task UpdateSingleGameAsync(InstalledGameEntry? entry)
    {
        if (entry == null)
            return;

        if (ActiveCachedRelease == null)
        {
            await DownloadLatestReleaseAsync();
            if (ActiveCachedRelease == null)
                return;
        }

        try
        {
            _installService.InstallOrUpdateGame(
                entry.GameName,
                entry.ExecutablePath,
                entry.Mode,
                Path.GetFileName(entry.InstalledFileName),
                ActiveCachedRelease,
                entry.Platform);
            RefreshLocalState();
            SetBanner(
                Loc.IsVietnamese ? $"Đã cập nhật {entry.GameName}" : $"Updated {entry.GameName}",
                Loc.IsVietnamese
                    ? $"{entry.GameName} đã được nâng cấp lên {ActiveCachedRelease.TagName}."
                    : $"{entry.GameName} is now on {ActiveCachedRelease.TagName}.");
        }
        catch (Exception ex)
        {
            SetBanner(Loc.IsVietnamese ? "Lỗi cập nhật" : "Update error", ex.Message);
        }
    }

    [RelayCommand]
    public void ReconfigureGameInWizard(InstalledGameEntry? entry)
    {
        if (entry == null)
            return;

        IsGameDetailVisible = false;
        IsInstallWizardVisible = true;
        WizardExecutablePath = entry.ExecutablePath;
        WizardGameName = entry.GameName;
        WizardSelectedMode = entry.Mode;
    }

    [RelayCommand]
    public async Task UninstallGameAsync(InstalledGameEntry? entry)
    {
        if (entry == null)
            return;

        var confirmed = await ConfirmAsync(
            Loc.IsVietnamese ? "Gỡ MFG Unlocker?" : "Uninstall MFG Unlocker?",
            Loc.IsVietnamese
                ? $"Gỡ MFG Unlocker khỏi \"{entry.GameName}\"?\n\nFile {entry.InstalledFileName} sẽ bị xóa (file *Hooked.dll gốc sẽ được khôi phục nếu có). Cấu hình RTXMFG-Universal.json được giữ lại."
                : $"Uninstall MFG Unlocker from \"{entry.GameName}\"?\n\n{entry.InstalledFileName} will be deleted (original *Hooked.dll restored if any). RTXMFG-Universal.json is kept.",
            Loc.IsVietnamese ? "Gỡ" : "Uninstall");
        if (!confirmed)
            return;

        try
        {
            _installService.UninstallGame(entry, removeSettingsAndLogs: false, removeFromList: true);
            if (SelectedGame == entry)
            {
                IsGameDetailVisible = false;
                SelectedGame = null;
            }
            RefreshLocalState();
            SetBanner(
                Loc.IsVietnamese ? $"Đã gỡ MFG Unlocker khỏi {entry.GameName}" : $"Uninstalled from {entry.GameName}",
                Loc.IsVietnamese
                    ? "Đã xóa DLL mod (và khôi phục file *Hooked.dll gốc nếu có). File cấu hình RTXMFG-Universal.json vẫn được giữ lại."
                    : "Removed mod DLL (and restored *Hooked.dll if applicable). Kept RTXMFG-Universal.json settings.");
        }
        catch (Exception ex)
        {
            SetBanner(Loc.IsVietnamese ? "Lỗi gỡ cài đặt" : "Uninstall error", ex.Message);
        }
    }

    [RelayCommand]
    public void OpenGameFolder(InstalledGameEntry? entry)
    {
        var dir = entry?.InstallDirectory;
        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    public void OpenGameDetails(InstalledGameEntry? entry)
    {
        if (entry == null)
            return;

        SelectedGame = entry;
        IsInstallWizardVisible = false;
        IsGameDetailVisible = true;

        var cfg = MfgConfigService.LoadConfig(entry.InstallDirectory);
        SelectedHotkeyOption = HotkeyOptions.FirstOrDefault(h => h.VkCode == cfg.ToggleKey) ?? HotkeyOptions[0];
        SelectedOptiScalerUiOption = OptiScalerUiOptions.FirstOrDefault(o => o.Value.Equals(cfg.OptiScalerUi, StringComparison.OrdinalIgnoreCase)) ?? OptiScalerUiOptions[0];
        ConfigGpuNameOverride = cfg.GpuNameOverride;
        ConfigRtx30FlipMetering = cfg.Rtx30FlipMetering;
        ConfigEnableRtxHair = cfg.EnableRtxHair;
        ConfigRawUniversalJson = cfg.RawUniversalJson;

        ReloadSelectedGameLogs();
    }

    [RelayCommand]
    public void CloseGameDetails()
    {
        IsGameDetailVisible = false;
    }

    [RelayCommand]
    public void SaveSelectedGameConfig()
    {
        if (SelectedGame == null)
            return;

        try
        {
            var cfg = new MfgGameConfig
            {
                ToggleKey = SelectedHotkeyOption?.VkCode ?? 8,
                OptiScalerUi = SelectedOptiScalerUiOption?.Value ?? string.Empty,
                GpuNameOverride = ConfigGpuNameOverride,
                Rtx30FlipMetering = ConfigRtx30FlipMetering,
                EnableRtxHair = ConfigEnableRtxHair,
                RawUniversalJson = ConfigRawUniversalJson
            };

            MfgConfigService.SaveConfig(SelectedGame.InstallDirectory, cfg);
            ConfigRawUniversalJson = cfg.RawUniversalJson;

            SetBanner(
                Loc.IsVietnamese ? "Đã lưu cấu hình" : "Configuration saved",
                Loc.IsVietnamese
                    ? $"Đã cập nhật RTXMFG-Universal.json & RTX40MFG-UI.ini cho {SelectedGame.GameName}."
                    : $"Saved RTXMFG-Universal.json & RTX40MFG-UI.ini for {SelectedGame.GameName}.");
        }
        catch (Exception ex)
        {
            SetBanner(Loc.IsVietnamese ? "Lỗi lưu cấu hình" : "Error saving config", ex.Message);
        }
    }

    [RelayCommand]
    public void ReloadSelectedGameLogs()
    {
        SelectedGameLogs.Clear();
        SelectedLogContent = string.Empty;
        if (SelectedGame == null)
            return;

        var logs = InstallService.GetGameLogFiles(SelectedGame.InstallDirectory);
        foreach (var l in logs)
        {
            SelectedGameLogs.Add(l);
        }

        SelectedLogFilePath = SelectedGameLogs.FirstOrDefault();
        if (SelectedLogFilePath == null)
        {
            SelectedLogContent = Loc.IsVietnamese
                ? "Chưa có file RTXMFG-*.log nào trong thư mục game. Hãy chạy game ít nhất một lần với MFG Unlocker."
                : "No RTXMFG-*.log files found yet. Launch the game at least once with MFG Unlocker.";
        }
    }

    private void LoadSelectedLogFileContent(string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            return;

        try
        {
            using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs);
            var text = reader.ReadToEnd();
            SelectedLogContent = text.Length > 30000 ? text[^30000..] : text;
        }
        catch (Exception ex)
        {
            SelectedLogContent = ex.Message;
        }
    }

    [RelayCommand]
    public void ExportDiagnosticsPackage()
    {
        if (SelectedGame == null)
            return;

        var dlg = new SaveFileDialog
        {
            Title = Loc.IsVietnamese ? "Xuất file ZIP chẩn đoán để báo lỗi" : "Export Diagnostics ZIP for Bug Report",
            Filter = "ZIP Archive (*.zip)|*.zip",
            FileName = $"MFG-Diagnostics-{SelectedGame.GameName.Replace(' ', '_')}-{DateTime.Now:yyyyMMdd-HHmm}.zip"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                InstallService.ExportDiagnosticsZip(SelectedGame, dlg.FileName);
                SetBanner(
                    Loc.IsVietnamese ? "Đã xuất gói ZIP chẩn đoán" : "Exported Diagnostics ZIP",
                    dlg.FileName);
            }
            catch (Exception ex)
            {
                SetBanner("Error", ex.Message);
            }
        }
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _manifestStore.Manifest.CheckUpdatesOnStartup = SettingsCheckUpdatesOnStartup;
        _manifestStore.Manifest.GitHubToken = string.IsNullOrWhiteSpace(SettingsGitHubToken) ? null : SettingsGitHubToken.Trim();
        _manifestStore.Save();

        SetBanner(
            Loc.IsVietnamese ? "Đã lưu cài đặt" : "Settings saved",
            Loc.IsVietnamese ? "Cấu hình ứng dụng đã được lưu." : "Application settings saved.");
    }

    [RelayCommand]
    public void OpenCacheDirectory()
    {
        if (Directory.Exists(_manifestStore.ReleasesCacheDirectory))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _manifestStore.ReleasesCacheDirectory,
                UseShellExecute = true
            });
        }
    }

    [RelayCommand]
    public async Task ClearCacheAsync()
    {
        var confirmed = await ConfirmAsync(
            Loc.IsVietnamese ? "Xóa toàn bộ Cache?" : "Clear all cache?",
            Loc.IsVietnamese
                ? "Tất cả bản ZIP/DLL đã tải sẽ bị xóa. Các game đã cài không bị ảnh hưởng, nhưng bạn cần tải lại bản phát hành trước khi cài/cập nhật."
                : "All downloaded ZIP/DLL releases will be deleted. Installed games are not affected, but you will need to download a release again before installing/updating.",
            Loc.IsVietnamese ? "Xóa" : "Clear");
        if (!confirmed)
            return;

        _manifestStore.ClearReleaseCache();
        RefreshLocalState();
        SetBanner(
            Loc.IsVietnamese ? "Đã xóa bộ nhớ đệm" : "Cache cleared",
            Loc.IsVietnamese ? "Đã xóa tất cả các bản ZIP/DLL đã tải trong bộ nhớ đệm." : "Removed all cached release archives and DLLs.");
    }

    [RelayCommand]
    public void OpenGitHubRepo()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/dashdogy/MFG-Unlocker/releases",
            UseShellExecute = true
        });
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var box = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryText,
            CloseButtonText = Loc.IsVietnamese ? "Hủy" : "Cancel"
        };

        var result = await box.ShowDialogAsync();
        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private void SetBanner(string title, string message)
    {
        StatusTitle = title;
        StatusMessage = message;
        IsStatusOpen = true;
    }
}
