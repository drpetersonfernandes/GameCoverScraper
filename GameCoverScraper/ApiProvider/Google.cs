using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Web;
using GameCoverScraper.Managers;
using GameCoverScraper.Models;
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

    internal static string BuildRequestUrl(string searchQuery, SettingsManager settingsManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchQuery);
        var encodedSearchQuery = HttpUtility.UrlEncode(searchQuery.Trim());

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

    internal static GoogleSearchResult? DeserializeResponse(string json, JsonSerializerOptions jsonOptions)
    {
        return JsonSerializer.Deserialize<GoogleSearchResult>(json, jsonOptions);
    }

    internal static List<ImageData> MapToImageData(GoogleSearchResult? searchResults)
    {
        if (searchResults?.Items != null)
        {
            return searchResults.Items.Select(static item => new ImageData
            {
                ImagePath = item.Link,
                ImageName = FormatImageName(item.Title),
                ImageFileSize = item.Image is { ByteSize: > 0 }
                    ? Math.Round(item.Image.ByteSize / 1024.0, 2) + " KB"
                    : "Unknown",
                ImageEncodingFormat = item.Mime,
                ImageWidth = item.Image?.Width ?? 0,
                ImageHeight = item.Image?.Height ?? 0,
                ThumbnailWidth = 0,
                ThumbnailHeight = 0
            }).ToList();
        }

        return new List<ImageData>();
    }

    public static async Task<List<ImageData>> FetchImagesFromGoogleAsync(string searchQuery, SettingsManager settingsManager, CancellationToken cancellationToken = default)
    {
        var requestUrl = BuildRequestUrl(searchQuery, settingsManager);

        const string logMessagePrefix = $"{ProviderName} API";
        AppLogger.Log($"{logMessagePrefix} Request: GET {requestUrl}");

        using var response = await HttpClientHelper.Client.GetAsync(requestUrl, cancellationToken);

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
                case HttpStatusCode.BadRequest:
                    {
                        AppLogger.Log($"{logMessagePrefix} Bad Request (400).");
                        var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        var badRequestEx = new HttpRequestException("Response status code does not indicate success: 400 (Bad Request).", null, HttpStatusCode.BadRequest);
                        _ = BugReport.LogErrorAsync(badRequestEx, $"HTTP error when calling {ProviderName} API: Bad Request (400). Response body: {errorBody}");
                        throw new InvalidOperationException(
                            $"{ProviderName} API error: Invalid request. Please check your API key and Search Engine ID configuration.", badRequestEx);
                    }
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            AppLogger.Log($"{logMessagePrefix} Response Body:\n{AppLogger.FormatJson(json)}");

            response.EnsureSuccessStatusCode();

            var deserializedResponse = DeserializeResponse(json, LogJsonSerializerOptions);
            var imageDataList = MapToImageData(deserializedResponse);

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
            _ = BugReport.LogErrorAsync(new OperationCanceledException($"{ProviderName} API request was cancelled."), $"{logMessagePrefix} request cancellation.");
            throw; // Re-throw cancellation
        }
    }

    internal static string FormatImageName(string input)
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
