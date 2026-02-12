using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Web;
using GameCoverScraper.Managers;
using GameCoverScraper.models;
using GameCoverScraper.Services;

namespace GameCoverScraper.ApiProvider;

public class Google
{
    private const int MaxResults = 10;
    private const string ProviderName = "Google";

    private static readonly JsonSerializerOptions LogJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static void LoadApiKeyFromSettings(SettingsManager settingsManager)
    {
        AppLogger.Log("Loading Google API key from settings...");

        if (!string.IsNullOrEmpty(settingsManager.GoogleKey))
        {
            AppLogger.Log("Google API key loaded from settings.");
            return;
        }

        AppLogger.Log("Google API Key is not set in settings.");
        throw new InvalidOperationException("Google API Key is not set. Please configure it in API Settings.");
    }

    private static string BuildRequestUrl(string searchQuery, SettingsManager settingsManager)
    {
        var encodedSearchQuery = HttpUtility.UrlEncode(searchQuery?.Trim() ?? "");
        if (string.IsNullOrEmpty(encodedSearchQuery))
        {
            throw new ArgumentException("Search query cannot be empty", nameof(searchQuery));
        }

        if (string.IsNullOrEmpty(settingsManager.GoogleSearchEngineId))
        {
            throw new InvalidOperationException("Google Search Engine ID is not configured");
        }

        if (string.IsNullOrEmpty(settingsManager.GoogleKey))
        {
            throw new InvalidOperationException("Google API Key is not configured");
        }

        AppLogger.Log($"Google Search Query: {searchQuery}");
        return $"https://www.googleapis.com/customsearch/v1?q={encodedSearchQuery}&cx={HttpUtility.UrlEncode(settingsManager.GoogleSearchEngineId)}&num={MaxResults}&searchType=image&key={HttpUtility.UrlEncode(settingsManager.GoogleKey)}";
    }

    private static GoogleSearchResult? DeserializeResponse(string json, JsonSerializerOptions jsonOptions)
    {
        return JsonSerializer.Deserialize<GoogleSearchResult>(json, jsonOptions);
    }

    private List<ImageData> MapToImageData(object? deserializedResponse, SettingsManager settingsManager)
    {
        var searchResults = deserializedResponse as GoogleSearchResult;
        if (searchResults?.Items != null)
        {
            return searchResults.Items.Select(item => new ImageData
            {
                ImagePath = item.Link,
                ImageName = FormatImageName(item.Title),
                ImageFileSize = Math.Round(item.Image.ByteSize / 1024.0, 2) + " KB",
                ImageEncodingFormat = item.Mime,
                ImageWidth = item.Image.Width,
                ImageHeight = item.Image.Height,
                ThumbnailWidth = 0,
                ThumbnailHeight = 0
            }).ToList();
        }

        return new List<ImageData>();
    }

    public async Task<List<ImageData>> FetchImagesFromGoogleAsync(string searchQuery, SettingsManager settingsManager, CancellationToken cancellationToken = default)
    {
        var requestUrl = BuildRequestUrl(searchQuery, settingsManager);
        var response = await HttpClientHelper.Client.GetAsync(requestUrl, cancellationToken);

        const string logMessagePrefix = $"{ProviderName} API";
        AppLogger.Log($"{logMessagePrefix} Request: GET {requestUrl}");

        try
        {
            AppLogger.Log($"{logMessagePrefix} Response Status: {response.StatusCode}");

            switch (response.StatusCode)
            {
                case HttpStatusCode.TooManyRequests:
                {
                    AppLogger.Log($"{logMessagePrefix} API rate limit exceeded (429).");
                    var rateLimitException = new HttpRequestException("Response status code does not indicate success: 429 (Too Many Requests).", null, HttpStatusCode.TooManyRequests);
                    _ = BugReport.LogErrorAsync(rateLimitException, $"HTTP error when calling {ProviderName} API: Rate limit exceeded.");
                    throw new InvalidOperationException($"{ProviderName} API rate limit has been exceeded. Please wait a moment before trying again.", rateLimitException);
                }
                case HttpStatusCode.Forbidden:
                {
                    AppLogger.Log($"{logMessagePrefix} Access Forbidden (403). Check API Key and API Permissions.");
                    var forbiddenEx = new HttpRequestException("403 (Forbidden)", null, HttpStatusCode.Forbidden);
                    _ = BugReport.LogErrorAsync(forbiddenEx, $"{ProviderName} API: Access Forbidden.");

                    throw new InvalidOperationException(
                        $"{ProviderName} API Access Forbidden (403).\n\n" +
                        "This usually means:\n" +
                        "1. Your API Key is incorrect.\n" +
                        "2. Your daily free limit has been reached.", forbiddenEx);
                }
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            AppLogger.Log($"{logMessagePrefix} Response Body:\n{AppLogger.FormatJson(json)}");

            response.EnsureSuccessStatusCode();

            var deserializedResponse = DeserializeResponse(json, LogJsonSerializerOptions);
            var imageDataList = MapToImageData(deserializedResponse, settingsManager);

            AppLogger.Log($"{logMessagePrefix} Successfully parsed {imageDataList.Count} images.");
            return imageDataList;
        }
        catch (HttpRequestException ex)
        {
            AppLogger.Log($"{logMessagePrefix} HTTP Error: {ex.StatusCode} - {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, $"HTTP error when calling {ProviderName} API: {ex.Message}");
            throw new InvalidOperationException($"{ProviderName} API error: {ex.Message}. Please check your API key or internet connection.", ex);
        }
        catch (JsonException ex)
        {
            _ = BugReport.LogErrorAsync(ex, $"Failed to deserialize {ProviderName} search results");
            throw new InvalidOperationException($"Failed to parse {ProviderName} API response. The service might be experiencing issues.", ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _ = BugReport.LogErrorAsync(ex, $"{ProviderName} API request timed out");
            throw new InvalidOperationException($"{ProviderName} API request timed out. Please try again.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            AppLogger.Log($"{logMessagePrefix} request was cancelled.");
            throw; // Re-throw cancellation
        }
    }

    private static string FormatImageName(string input)
    {
        if (Uri.IsWellFormedUriString(input, UriKind.Absolute))
        {
            try
            {
                var uri = new Uri(input);
                var fileName = Path.GetFileNameWithoutExtension(uri.LocalPath);
                if (!string.IsNullOrEmpty(fileName))
                {
                    var textInfo = CultureInfo.CurrentCulture.TextInfo;
                    return textInfo.ToTitleCase(fileName.ToLower(CultureInfo.InvariantCulture)
                        .Replace("-", " ")
                        .Replace("_", " "));
                }
            }
            catch
            {
                // Fall back to title formatting if URL parsing fails
            }
        }

        var textInfoTitle = CultureInfo.CurrentCulture.TextInfo;
        return textInfoTitle.ToTitleCase(input.ToLower(CultureInfo.InvariantCulture)
            .Replace("-", " ")
            .Replace("_", " "));
    }
}