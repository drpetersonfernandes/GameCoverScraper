using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace GameCoverScraper.Services;

public static class ApplicationStatsService
{
    private const string StatsApiUrl = "https://www.purelogiccode.com/ApplicationStats/stats";
    private const string ApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    private const string ApplicationId = "gamecoverscraper";

    public static async Task RecordStartupAsync()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

            var payload = new
            {
                applicationId = ApplicationId,
                version
            };

            var jsonPayload = JsonSerializer.Serialize(payload);
            using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, StatsApiUrl);

            request.Content = content;
            request.Headers.Add("Authorization", $"Bearer {ApiKey}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var response = await HttpClientHelper.Client.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                AppLogger.Log("Application stats recorded successfully.");
            }
            else
            {
                AppLogger.Log($"Application stats API returned: {response.StatusCode}");
            }
        }
        catch (TaskCanceledException)
        {
            AppLogger.Log("Application stats request timed out.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to record application stats: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Failed to record application stats.");
        }
    }
}
