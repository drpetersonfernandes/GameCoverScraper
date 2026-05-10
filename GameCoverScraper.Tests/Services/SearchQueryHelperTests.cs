using FluentAssertions;
using GameCoverScraper.Services;
using Xunit;

namespace GameCoverScraper.Tests.Services;

public class SearchQueryHelperTests
{
    [Theory]
    [InlineData("Super Mario Bros. (USA)", "Super Mario Bros.")]
    [InlineData("Sonic the Hedgehog (Europe) (Rev 1)", "Sonic the Hedgehog")]
    [InlineData("Mega Man X (Japan)", "Mega Man X")]
    [InlineData("Street Fighter II (Brazil) (En,Fr,De)", "Street Fighter II")]
    public void CleanSearchQueryWithRegionTagsShouldRemoveThem(string input, string expected)
    {
        var result = SearchQueryHelper.CleanSearchQuery(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("Game Name [!]", "Game Name")]
    [InlineData("Game Name [b1]", "Game Name")]
    public void CleanSearchQueryWithBracketTagsShouldRemoveThem(string input, string expected)
    {
        var result = SearchQueryHelper.CleanSearchQuery(input);

        result.Should().Be(expected);
    }

    [Fact]
    public void CleanSearchQueryWithMultipleTagsShouldRemoveAll()
    {
        var result = SearchQueryHelper.CleanSearchQuery("Zelda no Densetsu (Japan) (Rev 1) [!]");

        result.Should().Be("Zelda no Densetsu");
    }

    [Fact]
    public void CleanSearchQueryWithNoTagsShouldReturnUnchanged()
    {
        var result = SearchQueryHelper.CleanSearchQuery("Simple Game Name");

        result.Should().Be("Simple Game Name");
    }

    [Fact]
    public void CleanSearchQueryWithOnlyTagsShouldReturnOriginalName()
    {
        var result = SearchQueryHelper.CleanSearchQuery("(USA)");

        result.Should().Be("(USA)");
    }

    [Fact]
    public void CleanSearchQueryWithVersionTagShouldRemoveIt()
    {
        var result = SearchQueryHelper.CleanSearchQuery("Contra (USA) (v1.1)");

        result.Should().Be("Contra");
    }

    [Fact]
    public void CleanSearchQueryWithUnlicensedTagShouldRemoveIt()
    {
        var result = SearchQueryHelper.CleanSearchQuery("Somari (Unl)");

        result.Should().Be("Somari");
    }

    [Fact]
    public void CleanSearchQueryWithNestedParenthesesShouldHandleCorrectly()
    {
        // The non-greedy regex matches up to the first closing paren, leaving the outer one.
        var result = SearchQueryHelper.CleanSearchQuery("Game (Region (Sub))");

        result.Should().Be("Game)");
    }

    [Fact]
    public void CleanSearchQueryWithEmptyStringShouldReturnEmpty()
    {
        var result = SearchQueryHelper.CleanSearchQuery("");

        result.Should().Be("");
    }

    [Fact]
    public void CleanSearchQueryWithMegaDriveTagShouldRemoveIt()
    {
        var result = SearchQueryHelper.CleanSearchQuery("Sonic (Mega Drive 4)");

        result.Should().Be("Sonic");
    }

    [Fact]
    public void CleanSearchQueryShouldTrimWhitespace()
    {
        var result = SearchQueryHelper.CleanSearchQuery("  Game Name  (USA)  ");

        result.Should().Be("Game Name");
    }

    [Theory]
    [InlineData("normal_file.txt", "normal_file.txt")]
    [InlineData("game (USA).bin", "game (USA).bin")]
    public void SanitizeFileNameWithSafeInputShouldReturnUnchanged(string input, string expected)
    {
        var result = SearchQueryHelper.SanitizeFileName(input);

        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SanitizeFileNameWithNullOrEmptyShouldReturnUnnamed(string? input)
    {
        var result = SearchQueryHelper.SanitizeFileName(input!);

        result.Should().Be("unnamed");
    }

    [Fact]
    public void SanitizeFileNameWithPathTraversalShouldRemoveIt()
    {
        var result = SearchQueryHelper.SanitizeFileName("../../../etc/passwd");

        result.Should().NotContain("..");
        result.Should().NotContain("/");
        result.Should().NotContain("\\");
    }

    [Fact]
    public void SanitizeFileNameWithDoubleDotTraversalShouldRemoveAll()
    {
        var result = SearchQueryHelper.SanitizeFileName("....");

        result.Should().NotContain("..");
    }

    [Fact]
    public void SanitizeFileNameWithForwardSlashShouldRemoveIt()
    {
        var result = SearchQueryHelper.SanitizeFileName("folder/file");

        result.Should().NotContain("/");
    }

    [Fact]
    public void SanitizeFileNameWithBackslashShouldRemoveIt()
    {
        var result = SearchQueryHelper.SanitizeFileName("folder\\file");

        result.Should().NotContain("\\");
    }

    [Fact]
    public void SanitizeFileNameWithMixedPathSeparatorsShouldRemoveAll()
    {
        var result = SearchQueryHelper.SanitizeFileName("path/to\\file");

        result.Should().NotContain("/");
        result.Should().NotContain("\\");
    }

    [Fact]
    public void SanitizeFileNameWithInvalidCharsShouldReplaceWithUnderscore()
    {
        var result = SearchQueryHelper.SanitizeFileName("file<name>test");

        result.Should().NotContain("<");
        result.Should().NotContain(">");
        result.Should().Contain("_");
    }

    [Fact]
    public void SanitizeFileNameWithTrailingDotsShouldTrimThem()
    {
        var result = SearchQueryHelper.SanitizeFileName("filename...");

        result.Should().NotEndWith(".");
    }

    [Fact]
    public void SanitizeFileNameWithLeadingWhitespaceShouldTrimIt()
    {
        var result = SearchQueryHelper.SanitizeFileName("  filename  ");

        result.Should().Be("filename");
    }

    [Fact]
    public void SanitizeFileNameWithOnlyInvalidCharsShouldReturnUnnamed()
    {
        var result = SearchQueryHelper.SanitizeFileName("///...");

        result.Should().Be("unnamed");
    }

    [Fact]
    public void SanitizeFileNameWithComplexTraversalShouldSanitizeCompletely()
    {
        var result = SearchQueryHelper.SanitizeFileName(@"..\..\Windows\System32");

        result.Should().NotContain("..");
        result.Should().NotContain("\\");
        result.Should().NotContain("/");
    }
}
