using CommunityToolkit.Mvvm.ComponentModel;

namespace MFGUnlockerManager.Services;

public sealed partial class LocalizationService : ObservableObject
{
    public static LocalizationService Instance { get; } = new();

    [ObservableProperty]
    private bool _isVietnamese = false;

    public void SetLanguage(string langCode)
    {
        IsVietnamese = !string.IsNullOrWhiteSpace(langCode) &&
                       langCode.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
        OnPropertyChanged(string.Empty);
    }

    public string NavHome => IsVietnamese ? "Trang chủ" : "Home";
    public string NavGames => IsVietnamese ? "Thư viện Game" : "Games Library";
    public string NavCompatibility => IsVietnamese ? "Bảng tương thích" : "Compatibility";
    public string NavReleases => IsVietnamese ? "Phiên bản & Bản dựng" : "Releases";
    public string NavSettings => IsVietnamese ? "Cài đặt" : "Settings";

    public string HomeTitle => IsVietnamese ? "Tổng quan MFG Unlocker" : "MFG Unlocker Overview";
    public string HomeSubtitle => IsVietnamese
        ? "Mở khóa DLSS Multi Frame Generation (lên tới 6x & Dynamic MFG), XeMFG và DLSS 5 cho các tựa game Windows x64"
        : "Unlock DLSS Multi Frame Generation (up to 6x & Dynamic MFG), XeMFG, and DLSS 5 controls for Windows x64 games";

    public string LatestReleaseHeader => IsVietnamese ? "Phiên bản mới nhất trên GitHub" : "Latest GitHub Release";
    public string ActiveCachedHeader => IsVietnamese ? "Bản DLL đã tải trong máy" : "Cached Local DLL";
    public string InstalledGamesCountHeader => IsVietnamese ? "Số game đang quản lý" : "Managed Games";

    public string BtnCheckUpdate => IsVietnamese ? "Kiểm tra bản mới" : "Check for Updates";
    public string BtnDownloadLatest => IsVietnamese ? "Tải & Xác thực SHA-256" : "Download & Verify SHA-256";
    public string BtnUpdateAllGames => IsVietnamese ? "Cập nhật tất cả game" : "Update All Games";
    public string BtnInstallNewGame => IsVietnamese ? "Cài vào Game mới" : "Install to Game";
    public string BtnAutoScanGames => IsVietnamese ? "Quét tất cả Game" : "Scan All Games";
    public string BtnBrowseExe => IsVietnamese ? "Chọn file .exe của game..." : "Browse Game .exe...";
}
