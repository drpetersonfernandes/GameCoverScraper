using FluentAssertions;
using GameCoverScraper.Services;
using Xunit;

namespace GameCoverScraper.Tests.Services;

public class WebSearchServiceTests
{
    [Theory]
    [InlineData("Super Mario Bros", "Super+Mario+Bros")]
    [InlineData("game cover art", "game+cover+art")]
    [InlineData("", "")]
    [InlineData("special & chars", "special+%26+chars")]
    public void BuildBingSearchUrlShouldReturnCorrectUrl(string query, string expectedEncodedQuery)
    {
        var result = WebSearchService.BuildBingSearchUrl(query);

        result.Should().Be($"https://www.bing.com/images/search?q={expectedEncodedQuery}");
    }

    [Theory]
    [InlineData("Zelda", "Zelda")]
    [InlineData("mega man x", "mega+man+x")]
    [InlineData("", "")]
    public void BuildGoogleSearchUrlShouldReturnCorrectUrl(string query, string expectedEncodedQuery)
    {
        var result = WebSearchService.BuildGoogleSearchUrl(query);

        result.Should().Be($"https://www.google.com/search?tbm=isch&q={expectedEncodedQuery}");
    }
}
