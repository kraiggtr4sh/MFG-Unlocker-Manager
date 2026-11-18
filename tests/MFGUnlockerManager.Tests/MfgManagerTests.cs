using System.Text;
using MFGUnlockerManager.Models;
using MFGUnlockerManager.Services;
using Xunit;

namespace MFGUnlockerManager.Tests;

public class MfgManagerTests
{
    [Fact]
    public void CompatibilityParser_ParsesBundledTableAndFindsGames()
    {
        var service = new CompatibilityService();
        Assert.NotEmpty(service.Entries);

        var cp2077 = service.FindMatchingGame("Cyberpunk2077.exe");
        Assert.NotNull(cp2077);
        Assert.Equal("Cyberpunk 2077", cp2077.GameName);
        Assert.Contains("xinput9_1_0.dll", cp2077.RecommendedUse);
        Assert.Contains("dxgi.dll", cp2077.AlsoLoaded);
        Assert.Contains("version.dll", cp2077.DoNotUse);
        Assert.True(cp2077.SupportsOptiScaler);

        var wukong = service.FindMatchingGame("b1-Win64-Shipping.exe");
        Assert.NotNull(wukong);
        Assert.Equal("Black Myth: Wukong", wukong.GameName);
        Assert.Contains("version.dll", wukong.RecommendedUse);
    }

    [Fact]
    public void Sha256Parser_And_VersionComparison_WorkCorrectly()
    {
        var shaText = """
            C85CB2A9E74F39BE744FADA4A1A75D72C606F73315114F334AD7755A3A13005D  MFGUnlocker-v1.7.1.zip
            5A389E6CAD23B2B607E1DC224E9E86EE32D6EF46F0557B6C6F8137FEADF0AA74  MFGUnlocker.dll
            """;

        var map = GitHubReleaseService.ParseSha256Sums(shaText);
        Assert.Equal(2, map.Count);
        Assert.Equal("c85cb2a9e74f39be744fada4a1a75d72c606f73315114f334ad7755a3a13005d", map["MFGUnlocker-v1.7.1.zip"]);
        Assert.Equal("5a389e6cad23b2b607e1dc224e9e86ee32d6ef46f0557b6c6f8137feadf0aa74", map["MFGUnlocker.dll"]);

        Assert.True(GitHubReleaseService.CompareVersionTags("v1.6.0", "v1.7.1") < 0);
        Assert.True(GitHubReleaseService.CompareVersionTags("v1.7.1", "v1.7.1") == 0);
        Assert.True(GitHubReleaseService.CompareVersionTags("v1.8.0", "v1.7.1") > 0);
    }

    [Fact]
    public void InstallService_Standalone_BinkHook_OptiScaler_And_Uninstall_WorkSafely()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "MFGTests_" + Guid.NewGuid().ToString("N"));
        var appDataDir = Path.Combine(tempRoot, "AppData");
        var gameDir = Path.Combine(tempRoot, "FakeGame");
        Directory.CreateDirectory(appDataDir);
        Directory.CreateDirectory(gameDir);

        try
        {
            var fakeExe = Path.Combine(gameDir, "FakeGame.exe");
            File.WriteAllText(fakeExe, "FAKE_EXE_BINARY");

            // Create a fake cached release DLL (v1.7.0) and a newer one (v1.7.1)
            var dllV170 = Path.Combine(appDataDir, "MFGUnlocker_170.dll");
            File.WriteAllText(dllV170, "MFG_UNLOCKER_DLL_V170");
            var rel170 = new CachedReleaseInfo
            {
                TagName = "v1.7.0",
                DllPath = dllV170,
                DllSha256 = CompatibilityService.ComputeFileSha256(dllV170)
            };

            var dllV171 = Path.Combine(appDataDir, "MFGUnlocker_171.dll");
            File.WriteAllText(dllV171, "MFG_UNLOCKER_DLL_V171");
            var rel171 = new CachedReleaseInfo
            {
                TagName = "v1.7.1",
                DllPath = dllV171,
                DllSha256 = CompatibilityService.ComputeFileSha256(dllV171)
            };

            var store = new ManifestStore(appDataDir);
            store.Manifest.CachedReleases.Add(rel170);
            store.Manifest.CachedReleases.Add(rel171);
            store.Manifest.LatestKnownVersionTag = "v1.7.1";
            store.Manifest.ActiveCachedVersionTag = "v1.7.1";
            store.Save();

            var compat = new CompatibilityService();
            var installer = new InstallService(store, compat);

            // 1. Standalone install v1.7.0 as version.dll
            var entry = installer.InstallOrUpdateGame("Fake Game", fakeExe, InstallMode.StandaloneProxy, "version.dll", rel170);
            Assert.True(File.Exists(Path.Combine(gameDir, "version.dll")));
            Assert.True(entry.HasUpdateAvailable); // Because latest is v1.7.1

            // 2. Upgrade to v1.7.1
            entry = installer.InstallOrUpdateGame("Fake Game", fakeExe, InstallMode.StandaloneProxy, "version.dll", rel171);
            Assert.False(entry.HasUpdateAvailable);
            Assert.Equal("v1.7.1", entry.InstalledVersionTag);

            // 3. Test foreign DLL protection: place a foreign dxgi.dll (e.g. ReShade)
            var foreignDxgi = Path.Combine(gameDir, "dxgi.dll");
            File.WriteAllText(foreignDxgi, "RESHADE_FOREIGN_DLL");
            Assert.Throws<InvalidOperationException>(() =>
                installer.InstallOrUpdateGame("Fake Game", fakeExe, InstallMode.StandaloneProxy, "dxgi.dll", rel171));

            // 4. Test Bink Hook route: place original bink2w64.dll
            var origBink = Path.Combine(gameDir, "bink2w64.dll");
            File.WriteAllText(origBink, "ORIGINAL_BINK_DLL");
            entry = installer.InstallOrUpdateGame("Fake Game", fakeExe, InstallMode.BinkHooked, "bink2w64.dll", rel171);

            Assert.Equal(InstallMode.BinkHooked, entry.Mode);
            Assert.Equal("bink2w64Hooked.dll", entry.HookedBackupFileName);
            Assert.Equal("ORIGINAL_BINK_DLL", File.ReadAllText(Path.Combine(gameDir, "bink2w64Hooked.dll")));
            Assert.Equal("MFG_UNLOCKER_DLL_V171", File.ReadAllText(origBink));
            // Old version.dll should have been cleaned up automatically when switching proxy
            Assert.False(File.Exists(Path.Combine(gameDir, "version.dll")));

            // 5. Uninstall Bink Hook -> should restore ORIGINAL_BINK_DLL to bink2w64.dll
            installer.UninstallGame(entry, removeSettingsAndLogs: false, removeFromList: true);
            Assert.True(File.Exists(origBink));
            Assert.False(File.Exists(Path.Combine(gameDir, "bink2w64Hooked.dll")));
            Assert.Equal("ORIGINAL_BINK_DLL", File.ReadAllText(origBink));

            // 6. Test OptiScaler Plugin mode
            File.WriteAllText(Path.Combine(gameDir, "OptiScaler.ini"), "[Plugins]\r\nLoadAsiPlugins = false\r\n");
            entry = installer.InstallOrUpdateGame("Fake Game", fakeExe, InstallMode.OptiScalerPlugin, "MFGUnlocker.asi", rel171);
            Assert.Equal(InstallMode.OptiScalerPlugin, entry.Mode);
            Assert.True(File.Exists(Path.Combine(gameDir, "OptiScaler", "plugins", "MFGUnlocker.asi")));
            Assert.Contains("LoadAsiPlugins = true", File.ReadAllText(Path.Combine(gameDir, "OptiScaler.ini")));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ConfigService_RoundTripsJsonAndIniFiles()
    {
        var tempGameDir = Path.Combine(Path.GetTempPath(), "MFGConfigTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempGameDir);

        try
        {
            var cfg = new MfgGameConfig
            {
                ToggleKey = 45, // Insert
                OptiScalerUi = "shared",
                GpuNameOverride = true,
                Rtx30FlipMetering = true,
                EnableRtxHair = false,
                RawUniversalJson = "{\"customSetting\": 123}"
            };

            MfgConfigService.SaveConfig(tempGameDir, cfg);

            var loaded = MfgConfigService.LoadConfig(tempGameDir);
            Assert.Equal(45, loaded.ToggleKey);
            Assert.Equal("shared", loaded.OptiScalerUi);
            Assert.True(loaded.GpuNameOverride);
            Assert.True(loaded.Rtx30FlipMetering);
            Assert.False(loaded.EnableRtxHair);
            Assert.Contains("customSetting", loaded.RawUniversalJson);
        }
        finally
        {
            if (Directory.Exists(tempGameDir))
                Directory.Delete(tempGameDir, recursive: true);
        }
    }

    [Fact]
    public void GameScanner_DetectsCrackedAndRepackGamesWithDlssMarkers_And_SkipsSystemFolders()
    {
        Assert.True(GameScannerService.IsSkippedFolder(@"C:\Windows"));
        Assert.True(GameScannerService.IsSkippedFolder(@"C:\Program Files\NVIDIA Corporation"));
        Assert.True(GameScannerService.IsSkippedFolder(@"C:\Program Files\Microsoft Visual Studio"));
        Assert.False(GameScannerService.IsSkippedFolder(@"D:\Games\Black Myth Wukong"));

        var tempLib = Path.Combine(Path.GetTempPath(), "MFGScanTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempLib);

        try
        {
            // 1. Fake cracked Unreal game with DLSS FrameGen + steam_emu.ini
            var crackedGameDir = Path.Combine(tempLib, "Black Myth Wukong Repack");
            var win64Dir = Path.Combine(crackedGameDir, "b1", "Binaries", "Win64");
            Directory.CreateDirectory(win64Dir);
            File.WriteAllText(Path.Combine(win64Dir, "b1-Win64-Shipping.exe"), "FAKE_SHIPPING_EXE");
            File.WriteAllText(Path.Combine(win64Dir, "nvngx_dlssg.dll"), "FAKE_DLSSG");
            File.WriteAllText(Path.Combine(win64Dir, "steam_emu.ini"), "[Settings]");

            // 2. Fake non-game folder (no exe or markers)
            var docsDir = Path.Combine(tempLib, "RandomDocs");
            Directory.CreateDirectory(docsDir);
            File.WriteAllText(Path.Combine(docsDir, "readme.txt"), "hello");

            // 3. Skipped system-like folder inside library
            var skippedDir = Path.Combine(tempLib, "Common Files");
            Directory.CreateDirectory(skippedDir);
            File.WriteAllText(Path.Combine(skippedDir, "tool.exe"), "FAKE");

            // 4. Fake Inno Setup desktop utility (only unins000.exe + app.exe) -> MUST be rejected!
            var innoAppDir = Path.Combine(tempLib, "SomeDesktopTool");
            Directory.CreateDirectory(innoAppDir);
            File.WriteAllText(Path.Combine(innoAppDir, "SomeDesktopTool.exe"), "FAKE_APP");
            File.WriteAllText(Path.Combine(innoAppDir, "unins000.exe"), "FAKE_UNINS");

            // 5. Fake Electron app (resources\app.asar) -> MUST be rejected!
            var electronDir = Path.Combine(tempLib, "CustomLauncherApp");
            Directory.CreateDirectory(Path.Combine(electronDir, "resources"));
            File.WriteAllText(Path.Combine(electronDir, "CustomLauncherApp.exe"), "FAKE_ELECTRON");
            File.WriteAllText(Path.Combine(electronDir, "resources", "app.asar"), "FAKE_ASAR");

            // 6. Fake Anti-Cheat game (has EasyAntiCheat_EOS.exe) -> MUST be rejected!
            var acGameDir = Path.Combine(tempLib, "SomeOnlineShooter");
            Directory.CreateDirectory(acGameDir);
            File.WriteAllText(Path.Combine(acGameDir, "Shooter-Win64-Shipping.exe"), "FAKE_GAME");
            File.WriteAllText(Path.Combine(acGameDir, "EasyAntiCheat_EOS.exe"), "FAKE_EAC");

            // 7. Fake Esports game (VALORANT / League of Legends / osu!) -> MUST be rejected!
            var esportsDir = Path.Combine(tempLib, "VALORANT");
            Directory.CreateDirectory(esportsDir);
            File.WriteAllText(Path.Combine(esportsDir, "VALORANT-Win64-Shipping.exe"), "FAKE_VAL");

            var compat = new CompatibilityService();
            var scanner = new GameScannerService(compat);
            var found = scanner.ScanLibraryFolder(tempLib);

            Assert.Single(found);
            var game = found[0];
            Assert.Equal("Black Myth: Wukong", game.GameName);
            Assert.True(game.HasDlssFrameGen);
            Assert.True(game.HasCrackOrRepackMarker);
            Assert.Equal(GamePlatformSource.Other, game.Platform);
            Assert.Contains("DLSS FG ✓", game.Badge);
            Assert.EndsWith("b1-Win64-Shipping.exe", game.ExecutablePath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempLib))
                Directory.Delete(tempLib, recursive: true);
        }
    }
}
