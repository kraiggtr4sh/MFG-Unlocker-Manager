using System.Text.Json.Serialization;

namespace MFGUnlockerManager.Models;

public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset PublishedAt { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; } = [];

    [JsonIgnore]
    public string FormattedDate => PublishedAt == default ? "" : PublishedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    [JsonIgnore]
    public GitHubAsset? ZipAsset => Assets.FirstOrDefault(a =>
        a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
        a.Name.Contains("MFGUnlocker", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public GitHubAsset? Sha256Asset => Assets.FirstOrDefault(a =>
        a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase) ||
        a.Name.Contains("SHA256", StringComparison.OrdinalIgnoreCase));
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }
}

public sealed class CachedReleaseInfo
{
    public string TagName { get; set; } = string.Empty;
    public string ZipPath { get; set; } = string.Empty;
    public string DllPath { get; set; } = string.Empty;
    public string ZipSha256 { get; set; } = string.Empty;
    public string DllSha256 { get; set; } = string.Empty;
    public DateTimeOffset DownloadedAt { get; set; }
}
