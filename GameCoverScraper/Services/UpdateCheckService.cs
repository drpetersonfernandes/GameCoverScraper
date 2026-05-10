using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace GameCoverScraper.Services;

public static class UpdateCheckService
{
    private const string GitHubReleasesUrl = "https://api.github.com/repos/drpetersonfernandes/GameCoverScraper/releases/latest";
    private const string ReleasesPageUrl = "https://github.com/drpetersonfernandes/GameCoverScraper/releases";

    public static async Task<UpdateInfo> CheckForUpdateAsync()
    {
        try
        {
            var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            if (currentVersion == null)
            {
                AppLogger.Log("Could not determine current application version.");
                return new UpdateInfo { IsUpdateAvailable = false };
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesUrl);
            request.Headers.Add("User-Agent", "GameCoverScraper");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await HttpClientHelper.Client.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Log($"GitHub API returned status code: {response.StatusCode}");
                return new UpdateInfo { IsUpdateAvailable = false };
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString();
            if (string.IsNullOrEmpty(tagName))
            {
                AppLogger.Log("GitHub release has no tag_name.");
                return new UpdateInfo { IsUpdateAvailable = false };
            }

            var latestVersion = ParseVersion(tagName);
            if (latestVersion == null)
            {
                AppLogger.Log($"Could not parse version from tag: {tagName}");
                return new UpdateInfo { IsUpdateAvailable = false };
            }

            var releaseUrl = root.TryGetProperty("html_url", out var htmlUrlProp)
                ? htmlUrlProp.GetString() ?? ReleasesPageUrl
                : ReleasesPageUrl;

            var releaseNotes = root.TryGetProperty("body", out var bodyProp)
                ? bodyProp.GetString() ?? string.Empty
                : string.Empty;

            var publishedAt = root.TryGetProperty("published_at", out var publishedProp)
                ? publishedProp.GetString() ?? string.Empty
                : string.Empty;

            var isUpdateAvailable = latestVersion > currentVersion;

            if (isUpdateAvailable)
            {
                AppLogger.Log($"Update available: current={currentVersion}, latest={latestVersion}");
            }
            else
            {
                AppLogger.Log($"Application is up to date (v{currentVersion}).");
            }

            return new UpdateInfo
            {
                IsUpdateAvailable = isUpdateAvailable,
                CurrentVersion = currentVersion.ToString(),
                LatestVersion = latestVersion.ToString(),
                ReleaseUrl = releaseUrl,
                ReleaseNotes = releaseNotes,
                PublishedAt = publishedAt
            };
        }
        catch (TaskCanceledException)
        {
            AppLogger.Log("Update check timed out.");
            return new UpdateInfo { IsUpdateAvailable = false };
        }
        catch (HttpRequestException ex)
        {
            AppLogger.Log($"Update check failed (network error): {ex.Message}");
            return new UpdateInfo { IsUpdateAvailable = false };
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Update check failed: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Error checking for updates.");
            return new UpdateInfo { IsUpdateAvailable = false };
        }
    }

    private static Version? ParseVersion(string tagName)
    {
        var versionString = tagName.TrimStart('v', 'V');

        // Try parsing as-is
        if (Version.TryParse(versionString, out var version))
        {
            return version;
        }

        // Try appending .0 if only major.minor
        if (Version.TryParse(versionString + ".0", out version))
        {
            return version;
        }

        return null;
    }
}

public class UpdateInfo
{
    public bool IsUpdateAvailable { get; set; }
    public string CurrentVersion { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string ReleaseUrl { get; set; } = string.Empty;
    public string ReleaseNotes { get; set; } = string.Empty;
    public string PublishedAt { get; set; } = string.Empty;
}
