using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace ClickRun.Updates;

/// <summary>
/// Checks for updates from GitHub Releases.
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private readonly string _repoOwner;
    private readonly string _repoName;
    private readonly Version _currentVersion;

    public UpdateChecker(string repoOwner, string repoName, Version currentVersion, ILogger logger)
    {
        _repoOwner = repoOwner;
        _repoName = repoName;
        _currentVersion = currentVersion;
        _logger = logger.ForContext<UpdateChecker>();

        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", $"ClickRun/{currentVersion}");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Checks GitHub for the latest release.
    /// Returns update info if a newer version is available, null otherwise.
    /// </summary>
    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{_repoOwner}/{_repoName}/releases/latest";
            _logger.Debug("Checking for updates at {Url}", url);

            var response = await _httpClient.GetAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning("GitHub API returned {StatusCode}", response.StatusCode);
                return null;
            }

            var release = await response.Content.ReadFromJsonAsync<GitHubRelease>();
            if (release == null)
            {
                _logger.Warning("Failed to parse GitHub release response");
                return null;
            }

            // Parse version from tag (e.g., "v1.4.0" -> "1.4.0")
            var tagVersion = release.TagName.TrimStart('v', 'V');
            if (!Version.TryParse(tagVersion, out var latestVersion))
            {
                _logger.Warning("Failed to parse version from tag: {Tag}", release.TagName);
                return null;
            }

            _logger.Debug("Current: {Current}, Latest: {Latest}", _currentVersion, latestVersion);

            if (latestVersion <= _currentVersion)
            {
                _logger.Debug("Already up to date");
                return null;
            }

            // Find the exe or zip asset
            var asset = release.Assets?.FirstOrDefault(a =>
                a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

            if (asset == null)
            {
                _logger.Warning("No downloadable asset found in release");
                return null;
            }

            return new UpdateInfo
            {
                CurrentVersion = _currentVersion,
                LatestVersion = latestVersion,
                ReleaseNotes = release.Body ?? "",
                DownloadUrl = asset.BrowserDownloadUrl,
                AssetName = asset.Name,
                AssetSize = asset.Size,
                ReleaseUrl = release.HtmlUrl
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning(ex, "Network error checking for updates");
            return null;
        }
        catch (TaskCanceledException)
        {
            _logger.Warning("Update check timed out");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Unexpected error checking for updates");
            return null;
        }
    }

    /// <summary>
    /// Downloads the update to a temporary file.
    /// </summary>
    public async Task<string?> DownloadUpdateAsync(UpdateInfo update, IProgress<int>? progress = null)
    {
        try
        {
            _logger.Information("Downloading update from {Url}", update.DownloadUrl);

            var response = await _httpClient.GetAsync(update.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;
            var tempPath = Path.Combine(Path.GetTempPath(), $"ClickRun_Update_{update.LatestVersion}_{update.AssetName}");

            await using var contentStream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                totalRead += bytesRead;

                if (totalBytes > 0)
                {
                    var percentComplete = (int)((totalRead * 100) / totalBytes);
                    progress?.Report(percentComplete);
                }
            }

            _logger.Information("Update downloaded to {Path}", tempPath);
            return tempPath;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to download update");
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

/// <summary>
/// Information about an available update.
/// </summary>
public sealed class UpdateInfo
{
    public required Version CurrentVersion { get; init; }
    public required Version LatestVersion { get; init; }
    public required string ReleaseNotes { get; init; }
    public required string DownloadUrl { get; init; }
    public required string AssetName { get; init; }
    public required long AssetSize { get; init; }
    public required string ReleaseUrl { get; init; }

    public string FormattedSize => AssetSize switch
    {
        < 1024 => $"{AssetSize} B",
        < 1024 * 1024 => $"{AssetSize / 1024.0:F1} KB",
        _ => $"{AssetSize / (1024.0 * 1024.0):F1} MB"
    };
}

/// <summary>
/// GitHub Release API response model.
/// </summary>
internal sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubAsset>? Assets { get; set; }
}

/// <summary>
/// GitHub Release Asset model.
/// </summary>
internal sealed class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }
}
