# Universal MFG Unlocker Manager

A native **Windows 11 Fluent (Mica + Segoe UI Variable)** desktop installer, updater, and configuration manager for [dashdogy/MFG-Unlocker](https://github.com/dashdogy/MFG-Unlocker).

## Features

1. **Windows 11 Fluent Design UI**:
   - Native **Mica** window backdrop (`FluentWindow`), rounded corners across all controls, **Dark / Light Mode**, and Windows 11 Blue (`#0067C0`) accent styling.
   - Uses the Windows 11 system UI font (**Segoe UI Variable Text** / **Segoe UI Variable Display** with fallback to **Segoe UI**).
2. **Automated Release Asset Updates & SHA-256 Verification**:
   - Fetches releases from [dashdogy/MFG-Unlocker/releases](https://github.com/dashdogy/MFG-Unlocker/releases) (with automatic redirect fallback if GitHub REST API rate limits are reached).
   - Downloads `MFGUnlocker-vX.Y.Z.zip` and `SHA256SUMS.txt`, verifying **SHA-256 checksums** for both the archive and extracted `MFGUnlocker.dll` into `%LOCALAPPDATA%\MFGUnlockerManager\releases`.
   - Supports one-click bulk updates across all managed games or rolling back to earlier versions.
3. **Smart Install Wizard & Safe DLL Management**:
   - **Auto-recommended Proxy DLLs** (`version.dll`, `winmm.dll`, `dinput8.dll`, `xinput9_1_0.dll`, `d3d12.dll`, `dxgi.dll`, etc.) powered by live synchronization with `docs/compatibility.md`.
   - **Foreign DLL Overwrite Protection**: Detects and locks proxy filenames already occupied by game binaries or other mods (ReShade, Cyber Engine Tweaks, RED4ext, REFramework).
   - **Automated Bink Hook Route (`bink2w64.dll` / `binkw64.dll`)**: Automatically backs up original Bink DLLs to `bink2w64Hooked.dll` / `binkw64Hooked.dll` on install and restores them on uninstall.
   - **OptiScaler Plugin Mode (`OptiScaler\plugins\MFGUnlocker.asi`)**: Detects `OptiScaler.ini` in the game directory, installs the `.asi` plugin, and enables `LoadAsiPlugins = true` under `[Plugins]`.
   - **Launcher Stub & Anti-Cheat Detection**: Automatically suggests the real game binary (`Binaries\Win64\*-Win64-Shipping.exe` or `bin\x64_dx12`) when a root launcher is selected, and warns when EasyAntiCheat / BattlEye is present.
4. **Intelligent Game Scanner**:
   - Scans **Steam**, **Epic Games**, **Xbox / Game Pass**, **GOG Galaxy**, and **Standalone / Repack / Custom Game Folders** (`X:\Games` and custom folders).
   - Uses engine & graphics signature detection (`nvngx_dlssg.dll`, `sl.dlss_g.dll`, `nvngx_dlss.dll`, Unreal/Unity/REDengine/RE Engine markers, and Steam emulator markers) while automatically filtering out desktop apps, Electron apps, esports titles, and anti-cheat games.
   - Prioritizes games with native **DLSS FG ✓** support at the top of the list.
5. **Configuration Editor & Diagnostic Log Exporter**:
   - Edit `RTXMFG-Universal.json`, `RTX40MFG-UI.ini` (`ToggleKey`, `GpuNameOverride=1` for RTX 30/20 FG menu unlock, `Rtx30FlipMetering=1`), and `RTXMFG.RtxHair.ini` (`Universal=1/0`).
   - Inspect `RTXMFG-*.log` files directly and export a diagnostic `.zip` bundle for bug reports.

## Build & Run

```powershell
# Run unit tests
dotnet test MFGUnlockerManager.sln

# Run application
dotnet run --project src\MFGUnlockerManager\MFGUnlockerManager.csproj

# Publish release build
dotnet publish src\MFGUnlockerManager\MFGUnlockerManager.csproj -c Release -o publish
```
