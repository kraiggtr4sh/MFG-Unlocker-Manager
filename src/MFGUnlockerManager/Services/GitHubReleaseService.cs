using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using MFGUnlockerManager.Models;

namespace MFGUnlockerManager.Services;

public sealed class GitHubReleaseService
{
    public const string RepoOwner = "dashdogy";
    public const string RepoName = "MFG-Unlocker";
    private const string ReleasesApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";
    private const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
    private const string RawCompatibilityUrl = $"https://raw.githubusercontent.com/{RepoOwner}/{RepoName}/main/docs/compatibility.md";

    private readonly HttpClient _httpClient;
    private readonly ManifestStore _manifestStore;

    public GitHubReleaseService(ManifestStore manifestStore, HttpClient? httpClient = null)
    {
        _manifestStore = manifestStore;
        _httpClient = httpClient ?? CreateDefaultHttpClient();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MFGUnlockerManager", "1.0.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private void ApplyAuthHeader(HttpRequestMessage request)
    {
        var token = _manifestStore.Manifest.GitHubToken;
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }
    }

    public async Task<GitHubRelease?> FetchLatestReleaseAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        ApplyAuthHeader(req);
        using var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var fallback = await FetchLatestViaWebRedirectFallbackAsync(ct);
            if (fallback != null)
                return fallback;
            resp.EnsureSuccessStatusCode();
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var release = JsonSerializer.Deserialize<GitHubRelease>(json);
        if (release != null && !string.IsNullOrWhiteSpace(release.TagName))
        {
            _manifestStore.Manifest.LatestKnownVersionTag = release.TagName;
            _manifestStore.Save();
        }
        return release;
    }

    public async Task<List<GitHubRelease>> FetchAllReleasesAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ReleasesApiUrl}?per_page=20");
        ApplyAuthHeader(req);
        using var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var fallback = await FetchLatestViaWebRedirectFallbackAsync(ct);
            if (fallback != null)
                return [fallback];
            resp.EnsureSuccessStatusCode();
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? [];
        if (releases.Count > 0 && !string.IsNullOrWhiteSpace(releases[0].TagName))
        {
            _manifestStore.Manifest.LatestKnownVersionTag = releases[0].TagName;
            _manifestStore.Save();
        }
        return releases;
    }

    private async Task<GitHubRelease?> FetchLatestViaWebRedirectFallbackAsync(CancellationToken ct = default)
    {
        try
        {
            var webLatestUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/latest";
            using var req = new HttpRequestMessage(HttpMethod.Head, webLatestUrl);
            using var resp = await _httpClient.SendAsync(req, ct);
            var finalUri = resp.RequestMessage?.RequestUri?.ToString() ?? string.Empty;
            var tagMatch = Regex.Match(finalUri, @"/releases/tag/([^/?#]+)", RegexOptions.IgnoreCase);
            var tag = tagMatch.Success
                ? tagMatch.Groups[1].Value
                : (_manifestStore.Manifest.LatestKnownVersionTag ?? "v1.7.1");

            var zipName = $"MFGUnlocker-{tag}.zip";
            var release = new GitHubRelease
            {
                TagName = tag,
                Name = $"Universal MFG Unlocker - {tag.ToUpperInvariant()}",
                HtmlUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/tag/{tag}",
                PublishedAt = DateTimeOffset.UtcNow,
                Body = $"Latest release {tag} from https://github.com/{RepoOwner}/{RepoName}/releases/tag/{tag}\n\nClick 'Download & Verify SHA-256' to download and verify MFGUnlocker.dll.",
                Assets =
                [
                    new GitHubAsset
                    {
                        Name = zipName,
                        BrowserDownloadUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/{zipName}"
                    },
                    new GitHubAsset
                    {
                        Name = "SHA256SUMS.txt",
                        BrowserDownloadUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/download/{tag}/SHA256SUMS.txt"
                    }
                ]
            };

            _manifestStore.Manifest.LatestKnownVersionTag = tag;
            _manifestStore.Save();
            return release;
        }
        catch
        {
            return null;
        }
    }

    public async Task<string?> FetchLatestCompatibilityMarkdownAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, RawCompatibilityUrl);
            ApplyAuthHeader(req);
            using var resp = await _httpClient.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                return null;

            var md = await resp.Content.ReadAsStringAsync(ct);
            if (!string.IsNullOrWhiteSpace(md))
            {
                await File.WriteAllTextAsync(_manifestStore.CompatibilityCacheFilePath, md, ct);
            }
            return md;
        }
        catch
        {
            if (File.Exists(_manifestStore.CompatibilityCacheFilePath))
            {
                return await File.ReadAllTextAsync(_manifestStore.CompatibilityCacheFilePath, ct);
            }
            return null;
        }
    }

    public static Dictionary<string, string> ParseSha256Sums(string sha256SumsText)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(sha256SumsText))
            return map;

        var lines = sha256SumsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var match = Regex.Match(line.Trim(), @"^([a-fA-F0-9]{64})\s+\*?(.+)$");
            if (match.Success)
            {
                var hash = match.Groups[1].Value.ToLowerInvariant();
                var fileName = Path.GetFileName(match.Groups[2].Value.Trim());
                map[fileName] = hash;
            }
        }
        return map;
    }

    public async Task<CachedReleaseInfo> DownloadAndVerifyReleaseAsync(
        GitHubRelease release,
        IProgress<(double Percent, string Status)>? progress = null,
        CancellationToken ct = default)
    {
        var zipAsset = release.ZipAsset
            ?? throw new InvalidOperationException($"No MFGUnlocker ZIP asset found in release {release.TagName}.");

        var tagFolder = Path.Combine(_manifestStore.ReleasesCacheDirectory, SanitizeFileName(release.TagName));
        Directory.CreateDirectory(tagFolder);

        var zipPath = Path.Combine(tagFolder, zipAsset.Name);
        var dllPath = Path.Combine(tagFolder, "MFGUnlocker.dll");

        // 1. Fetch SHA256SUMS.txt if available
        var expectedHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (release.Sha256Asset != null)
        {
            progress?.Report((5, $"Downloading {release.Sha256Asset.Name}..."));
            using var shaReq = new HttpRequestMessage(HttpMethod.Get, release.Sha256Asset.BrowserDownloadUrl);
            using var shaResp = await _httpClient.SendAsync(shaReq, ct);
            if (shaResp.IsSuccessStatusCode)
            {
                var shaText = await shaResp.Content.ReadAsStringAsync(ct);
                await File.WriteAllTextAsync(Path.Combine(tagFolder, "SHA256SUMS.txt"), shaText, ct);
                expectedHashes = ParseSha256Sums(shaText);
            }
        }

        // 2. Download ZIP with streaming progress
        progress?.Report((10, $"Downloading {zipAsset.Name}..."));
        using (var zipReq = new HttpRequestMessage(HttpMethod.Get, zipAsset.BrowserDownloadUrl))
        using (var zipResp = await _httpClient.SendAsync(zipReq, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            zipResp.EnsureSuccessStatusCode();
            var totalBytes = zipResp.Content.Headers.ContentLength ?? zipAsset.Size;

            await using var netStream = await zipResp.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await netStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                totalRead += read;
                if (totalBytes > 0)
                {
                    var pct = 10 + (totalRead * 65.0 / totalBytes);
                    progress?.Report((pct, $"Downloading {zipAsset.Name} ({totalRead / 1024} KB / {totalBytes / 1024} KB)..."));
                }
            }
        }

        // 3. Verify ZIP SHA-256
        progress?.Report((80, "Verifying ZIP SHA-256 checksum..."));
        var actualZipSha = CompatibilityService.ComputeFileSha256(zipPath);
        if (expectedHashes.TryGetValue(zipAsset.Name, out var expectedZipSha))
        {
            if (!actualZipSha.Equals(expectedZipSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(zipPath);
                throw new InvalidDataException(
                    $"SHA-256 mismatch for {zipAsset.Name}. Expected {expectedZipSha}, got {actualZipSha}.");
            }
        }

        // 4. Extract MFGUnlocker.dll
        progress?.Report((90, "Extracting MFGUnlocker.dll..."));
        if (File.Exists(dllPath))
        {
            File.Delete(dllPath);
        }

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var dllEntry = archive.Entries.FirstOrDefault(e =>
                e.Name.Equals("MFGUnlocker.dll", StringComparison.OrdinalIgnoreCase) ||
                e.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException($"No DLL found inside {zipAsset.Name}.");

            dllEntry.ExtractToFile(dllPath, overwrite: true);
        }

        // 5. Verify DLL SHA-256
        var actualDllSha = CompatibilityService.ComputeFileSha256(dllPath);
        if (expectedHashes.TryGetValue("MFGUnlocker.dll", out var expectedDllSha))
        {
            if (!actualDllSha.Equals(expectedDllSha, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(dllPath);
                throw new InvalidDataException(
                    $"SHA-256 mismatch for MFGUnlocker.dll. Expected {expectedDllSha}, got {actualDllSha}.");
            }
        }

        var cachedInfo = new CachedReleaseInfo
        {
            TagName = release.TagName,
            ZipPath = zipPath,
            DllPath = dllPath,
            ZipSha256 = actualZipSha,
            DllSha256 = actualDllSha,
            DownloadedAt = DateTimeOffset.UtcNow
        };

        _manifestStore.Manifest.CachedReleases.RemoveAll(r =>
            r.TagName.Equals(release.TagName, StringComparison.OrdinalIgnoreCase));
        _manifestStore.Manifest.CachedReleases.Add(cachedInfo);
        _manifestStore.Manifest.ActiveCachedVersionTag = release.TagName;
        _manifestStore.Manifest.LatestKnownVersionTag ??= release.TagName;
        _manifestStore.Save();

        progress?.Report((100, $"Ready: {release.TagName} (SHA-256 verified)"));
        return cachedInfo;
    }

    public static int CompareVersionTags(string? tagA, string? tagB)
    {
        var vA = ParseSemVer(tagA);
        var vB = ParseSemVer(tagB);
        return vA.CompareTo(vB);
    }

    private static Version ParseSemVer(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return new Version(0, 0, 0);

        var m = Regex.Match(tag, @"(\d+)\.(\d+)(?:\.(\d+))?");
        if (!m.Success)
            return new Version(0, 0, 0);

        int major = int.Parse(m.Groups[1].Value);
        int minor = int.Parse(m.Groups[2].Value);
        int patch = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        return new Version(major, minor, patch);
    }

    private static string SanitizeFileName(string input)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            input = input.Replace(c, '_');
        }
        return input;
    }
}
