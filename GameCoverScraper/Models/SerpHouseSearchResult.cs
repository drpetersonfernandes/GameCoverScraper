using System.Text.Json.Serialization;

namespace GameCoverScraper.models;

public class SerpHouseSearchResult
{
    [JsonPropertyName("image_results")]
    public List<SerpHouseImageResult>? ImageResults { get; set; }
}

public class SerpHouseImageResult
{
    [JsonPropertyName("link")]
    public required string Link { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Unknown Filename";

    [JsonPropertyName("thumbnail")]
    public required string Thumbnail { get; set; }
}