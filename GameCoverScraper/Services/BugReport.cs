using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using GameCoverScraper.Managers;

namespace GameCoverScraper.Services;

public class BugReportService
{
    private readonly SettingsManager _settings;
    internal static readonly string AppVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown";

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

        if (ex == null)
        {
            ex = new InvalidOperationException("LogErrorAsync was called with a null exception.");
        }

        var now = DateTime.Now;
        var osVersion = RuntimeInformation.OSDescription;
        var architecture = RuntimeInformation.OSArchitecture.ToString();
        var processorCount = Environment.ProcessorCount;
        var tempPath = Path.GetTempPath();
        var windowsVersion = Environment.OSVersion.VersionString;
        var is64Bit = Environment.Is64BitOperatingSystem;
        var bitness = is64Bit ? "64-bit" : "32-bit";

        var envDetails = new StringBuilder();
        envDetails.AppendLine("=== Environment Details ===");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Date: {now:yyyy-MM-dd HH:mm:ss}");
        envDetails.AppendLine("Application Name: GameCoverScraper");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Application Version: {AppVersion}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {osVersion}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Architecture: {architecture}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Bitness: {bitness}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Windows Version: {windowsVersion}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Processor Count: {processorCount}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Base Directory: {baseDirectory}");
        envDetails.AppendLine(CultureInfo.InvariantCulture, $"Temp Path: {tempPath}");

        if (!string.IsNullOrEmpty(contextMessage))
        {
            envDetails.AppendLine();
            envDetails.AppendLine(CultureInfo.InvariantCulture, $"Context: {contextMessage}");
        }

        var errorDetails = new StringBuilder();
        errorDetails.AppendLine();
        errorDetails.AppendLine("=== Error Details ===");
        errorDetails.AppendLine(CultureInfo.InvariantCulture, $"Error Message: {ex.Message}");

        var exceptionDetails = new StringBuilder();
        exceptionDetails.AppendLine();
        exceptionDetails.AppendLine("=== Exception Details ===");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.GetType().FullName}");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.Message}");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.Source ?? "N/A"}");
        exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.StackTrace ?? "N/A"}");

        if (ex.InnerException != null)
        {
            exceptionDetails.AppendLine();
            exceptionDetails.AppendLine("--- Inner Exception ---");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.InnerException.GetType().FullName}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.InnerException.Message}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.InnerException.Source ?? "N/A"}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.InnerException.StackTrace ?? "N/A"}");
        }

        var fullLog = $"{envDetails}\n{errorDetails}\n{exceptionDetails}\n";

        AppLogger.Log($"--- ERROR ---\n{fullLog}");

        try
        {
            await File.AppendAllTextAsync(errorLogPath, fullLog);

            var userErrorMessage = fullLog +
                                   "--------------------------------------------------------------------------------------------------------------\n\n\n";
            await File.AppendAllTextAsync(userLogPath, userErrorMessage);

            if (await SendLogToApiAsync(ex, contextMessage, envDetails.ToString()))
            {
                File.Delete(errorLogPath);
            }
        }
        catch (Exception loggingEx)
        {
            await Console.Error.WriteLineAsync($"Failed to write error log files or send to API: {loggingEx.Message}");
        }
    }

    private async Task<bool> SendLogToApiAsync(Exception ex, string? contextMessage, string envDetails)
    {
        if (string.IsNullOrEmpty(ApiKey))
        {
            await Console.Error.WriteLineAsync("API Key is missing. Cannot send error log.");
            return false;
        }

        var messageBuilder = new StringBuilder();
        messageBuilder.Append(envDetails);
        messageBuilder.AppendLine();
        messageBuilder.AppendLine("=== Error Details ===");
        messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"Error Message: {ex.Message}");

        if (!string.IsNullOrEmpty(contextMessage))
        {
            messageBuilder.AppendLine(CultureInfo.InvariantCulture, $"Context: {contextMessage}");
        }

        var message = messageBuilder.ToString();
        if (message.Length > 4000)
        {
            message = message[..3997] + "...";
        }

        var stackTraceBuilder = new StringBuilder();
        stackTraceBuilder.AppendLine("=== Exception Details ===");
        stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.GetType().FullName}");
        stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.Message}");
        stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.Source ?? "N/A"}");
        stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.StackTrace ?? "N/A"}");

        if (ex.InnerException != null)
        {
            stackTraceBuilder.AppendLine();
            stackTraceBuilder.AppendLine("--- Inner Exception ---");
            stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.InnerException.GetType().FullName}");
            stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.InnerException.Message}");
            stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.InnerException.Source ?? "N/A"}");
            stackTraceBuilder.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.InnerException.StackTrace ?? "N/A"}");
        }

        var stackTrace = stackTraceBuilder.ToString();
        if (stackTrace.Length > 8000)
        {
            stackTrace = stackTrace[..7997] + "...";
        }

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var bugReportPayload = new
                {
                    ApplicationName = "GameCoverScraper",
                    Version = AppVersion,
                    Message = message,
                    StackTrace = stackTrace,
                    Environment = RuntimeInformation.OSDescription[..Math.Min(200, RuntimeInformation.OSDescription.Length)]
                };

                var jsonPayload = JsonSerializer.Serialize(bugReportPayload);
                using var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
                using var request = new HttpRequestMessage(HttpMethod.Post, BugReportApiUrl);

                request.Content = content;
                request.Headers.Add("X-API-KEY", ApiKey);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                using var response = await HttpClient.SendAsync(request, cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                if ((int)response.StatusCode >= 500 && attempt < maxRetries)
                {
                    await Console.Error.WriteLineAsync(
                        $"API returned {response.StatusCode} (attempt {attempt}/{maxRetries}). Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), cts.Token);
                    continue;
                }

                var errorContent = await response.Content.ReadAsStringAsync(cts.Token);
                await Console.Error.WriteLineAsync($"API returned non-success status code: {response.StatusCode}");
                await Console.Error.WriteLineAsync($"API Error Response: {errorContent}");
                return false;
            }
            catch (HttpRequestException httpEx)
            {
                if (attempt < maxRetries)
                {
                    await Console.Error.WriteLineAsync(
                        $"HTTP Request failed (attempt {attempt}/{maxRetries}): {httpEx.Message}. Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
                    continue;
                }

                await Console.Error.WriteLineAsync($"HTTP Request failed when sending log to API: {httpEx.Message}");
                return false;
            }
            catch (TaskCanceledException tcEx) when (tcEx.CancellationToken.IsCancellationRequested)
            {
                if (attempt < maxRetries)
                {
                    await Console.Error.WriteLineAsync(
                        $"HTTP Request timed out (attempt {attempt}/{maxRetries}). Retrying...");
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)));
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
        if (_instance != null)
            return _instance.LogErrorAsync(ex, contextMessage);

        // Fallback: _instance not yet initialized (e.g. crash before BugReport.Initialize).
        // Write to local log files so the error is not silently swallowed.
        return LogErrorToLocalAsync(ex, contextMessage);
    }

    private static async Task LogErrorToLocalAsync(Exception? ex, string? contextMessage)
    {
        try
        {
            ex ??= new InvalidOperationException("LogErrorAsync was called with a null exception.");

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var errorLogPath = Path.Combine(baseDirectory, "error.log");
            var userLogPath = Path.Combine(baseDirectory, "error_user.log");

            var envDetails = new StringBuilder();
            envDetails.AppendLine("=== Environment Details ===");
            envDetails.AppendLine(CultureInfo.InvariantCulture, $"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            envDetails.AppendLine("Application Name: GameCoverScraper");
            envDetails.AppendLine(CultureInfo.InvariantCulture, $"Application Version: {BugReportService.AppVersion}");
            envDetails.AppendLine(CultureInfo.InvariantCulture, $"OS Version: {RuntimeInformation.OSDescription}");
            envDetails.AppendLine(CultureInfo.InvariantCulture, $"Architecture: {RuntimeInformation.OSArchitecture}");
            envDetails.AppendLine(CultureInfo.InvariantCulture, $"Base Directory: {baseDirectory}");

            if (!string.IsNullOrEmpty(contextMessage))
            {
                envDetails.AppendLine();
                envDetails.AppendLine(CultureInfo.InvariantCulture, $"Context: {contextMessage}");
            }

            var exceptionDetails = new StringBuilder();
            exceptionDetails.AppendLine();
            exceptionDetails.AppendLine("=== Exception Details ===");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.GetType().FullName}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.Message}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.Source ?? "N/A"}");
            exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.StackTrace ?? "N/A"}");

            if (ex.InnerException != null)
            {
                exceptionDetails.AppendLine();
                exceptionDetails.AppendLine("--- Inner Exception ---");
                exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Type: {ex.InnerException.GetType().FullName}");
                exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Message: {ex.InnerException.Message}");
                exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"Source: {ex.InnerException.Source ?? "N/A"}");
                exceptionDetails.AppendLine(CultureInfo.InvariantCulture, $"StackTrace: {ex.InnerException.StackTrace ?? "N/A"}");
            }

            var fullLog = $"{envDetails}\n{exceptionDetails}\n";

            AppLogger.Log($"--- ERROR (pre-init fallback) ---\n{fullLog}");

            await File.AppendAllTextAsync(errorLogPath, fullLog);

            var userErrorMessage = fullLog +
                                   "--------------------------------------------------------------------------------------------------------------\n\n\n";
            await File.AppendAllTextAsync(userLogPath, userErrorMessage);
        }
        catch
        {
            // If even local logging fails, nothing more we can do
        }
    }
}
