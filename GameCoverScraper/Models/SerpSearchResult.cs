using System.Text.Json.Serialization;

namespace GameCoverScraper.models;

public class SerpSearchResult
{
    [JsonPropertyName("search_metadata")]
    public SerpSearchMetadata? SearchMetadata { get; set; }

    [JsonPropertyName("images_results")]
    public List<SerpImageResult>? ImagesResults { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class SerpSearchMetadata
{
    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("total_time_taken")]
    public double? TotalTimeTaken { get; set; }

    [JsonPropertyName("bing_images_url")]
    public string? BingImagesUrl { get; set; }
}

public class SerpImageResult
{
    [JsonPropertyName("original")]
    public required string Original { get; set; }

    [JsonPropertyName("thumbnail")]
    public string? Thumbnail { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Unknown Filename";

    [JsonPropertyName("size")]
    public string Size { get; set; } = "0x0";

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("position")]
    public int Position { get; set; }
}