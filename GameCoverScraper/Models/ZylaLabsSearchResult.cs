using System.Text.Json.Serialization;

namespace GameCoverScraper.models;

public class ZylaLabsSearchResult
{
    [JsonPropertyName("results")]
    public List<ZylaLabsImageResult>? Results { get; set; }
}

public class ZylaLabsImageResult
{
    [JsonPropertyName("image")]
    public required string Image { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = "Unknown Filename";

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }
}