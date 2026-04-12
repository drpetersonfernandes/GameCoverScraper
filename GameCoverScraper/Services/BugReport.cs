using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using GameCoverScraper.Managers;

namespace GameCoverScraper.Services;

public interface IBugReportService
{
    Task LogErrorAsync(Exception? ex, string? contextMessage = null);
}

public class BugReportService : IBugReportService
{
    private readonly SettingsManager _settings;

    // Use shared HttpClient from HttpClientHelper to avoid resource leaks
    private static HttpClient HttpClient => HttpClientHelper.Client;

    public BugReportService(SettingsManager settingsManager)
    {
        _settings = settingsManager;
    }

    private string ApiKey => _settings.BugReportApiKey;
    private string BugReportApiUrl => _settings.BugReportApiUrl;

    public async Task LogErrorAsync(Exception? ex, string? contextMessage = null)
    {
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var errorLogPath = Path.Combine(baseDirectory, "error.log");
        var userLogPath = Path.Combine(baseDirectory, "error_user.log");
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

        // Include exception details in the log message
        var fullErrorMessage = $"Date: {DateTime.Now}\nVersion: {version}\n\n";
        if (!string.IsNullOrEmpty(contextMessage))
        {
            fullErrorMessage += $"{contextMessage}\n\n";
        }

        if (ex == null)
        {
            ex = new InvalidOperationException("LogErrorAsync was called with a null exception.");
        }

        fullErrorMessage += $"Exception Type: {ex.GetType().Name}\n";
        fullErrorMessage += $"Exception Message: {ex.Message}\n";
        fullErrorMessage += $"Stack Trace:\n{ex.StackTrace}\n\n";

        // Log to the main application logger
        AppLogger.Log($"--- ERROR ---\n{fullErrorMessage}");

        try
        {
            // Append the error message to the general log
            await File.AppendAllTextAsync(errorLogPath, fullErrorMessage);

            // Append the error message to the user-specific log
            var userErrorMessage = fullErrorMessage +
                                   "--------------------------------------------------------------------------------------------------------------\n\n\n";
            await File.AppendAllTextAsync(userLogPath, userErrorMessage);

            // Attempt to send the error log content to the new API.
            // Pass the full error message including exception details
            if (await SendLogToApiAsync(fullErrorMessage))
            {
                // If the log was successfully sent, delete the general log file to clean up.
                // Keep the user log file for the user's reference.
                File.Delete(errorLogPath);
            }
        }
        catch (Exception loggingEx)
        {
            // Ignore any exceptions raised during logging to avoid interrupting the main flow
            // Optionally log this failure to console or a separate minimal log file
            await Console.Error.WriteLineAsync($"Failed to write error log files or send to API: {loggingEx.Message}");
        }
    }

    private async Task<bool> SendLogToApiAsync(string logContent)
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            await Console.Error.WriteLineAsync("API Key is missing. Cannot send error log.");
            return false;
        }

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var bugReportPayload = new
                {
                    ApplicationName = "GameCoverScraper",
                    Message = logContent
                };

                var jsonPayload = JsonSerializer.Serialize(bugReportPayload);
                using var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, BugReportApiUrl);

                request.Content = content;
                request.Headers.Add("X-API-KEY", ApiKey);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var response = await HttpClient.SendAsync(request, cts.Token);

                // Simplified: if IsSuccessStatusCode, assume success
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
                else
                {
                    // For 5xx errors, retry; for 4xx errors, don't retry
                    if ((int)response.StatusCode >= 500 && attempt < maxRetries)
                    {
                        await Console.Error.WriteLineAsync(
                            $"API returned {response.StatusCode} (attempt {attempt}/{maxRetries}). Retrying...");
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cts.Token); // Exponential backoff
                        continue;
                    }

                    var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                    await Console.Error.WriteLineAsync($"API returned non-success status code: {response.StatusCode}");
                    await Console.Error.WriteLineAsync($"API Error Response: {errorContent}");
                    return false;
                }
            }
            catch (HttpRequestException httpEx)
            {
                // Handle network errors, DNS issues, connection refused, etc.
                if (attempt < maxRetries)
                {
                    await Console.Error.WriteLineAsync(
                        $"HTTP Request failed (attempt {attempt}/{maxRetries}): {httpEx.Message}. Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
                    continue;
                }

                await Console.Error.WriteLineAsync($"HTTP Request failed when sending log to API: {httpEx.Message}");
                return false;
            }
            catch (TaskCanceledException tcEx) when (tcEx.CancellationToken.IsCancellationRequested)
            {
                // Handle timeout
                if (attempt < maxRetries)
                {
                    await Console.Error.WriteLineAsync(
                        $"HTTP Request timed out (attempt {attempt}/{maxRetries}). Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt))); // Exponential backoff
                    continue;
                }

                await Console.Error.WriteLineAsync("HTTP Request timed out when sending log to API.");
                return false;
            }
            catch (JsonException jsonEx)
            {
                await Console.Error.WriteLineAsync(
                    $"JSON serialization error when sending log to API: {jsonEx.Message}");
                return false;
            }
            catch (IOException ioEx)
            {
                await Console.Error.WriteLineAsync(
                    $"I/O error when sending log to API: {ioEx.Message}");
                return false;
            }
            catch (InvalidOperationException opEx)
            {
                await Console.Error.WriteLineAsync(
                    $"Invalid operation when sending log to API: {opEx.Message}");
                return false;
            }
        }

        return false;
    }
}

// Legacy static class for backward compatibility during transition
// Deprecated: Use IBugReportService via dependency injection instead
public static class BugReport
{
    private static BugReportService? _instance;

    public static void Initialize(SettingsManager settingsManager)
    {
        _instance = new BugReportService(settingsManager);
    }

    public static Task LogErrorAsync(Exception? ex, string? contextMessage = null)
    {
        return _instance?.LogErrorAsync(ex, contextMessage) ?? Task.CompletedTask;
    }
}
