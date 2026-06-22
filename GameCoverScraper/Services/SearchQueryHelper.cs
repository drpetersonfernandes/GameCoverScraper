using System.Text.RegularExpressions;

namespace GameCoverScraper.Services;

internal static partial class SearchQueryHelper
{
    // Matches common patterns in parentheses or square brackets.
    // e.g., (USA), (Europe), (Japan), (Brazil), (En,Ja), [!], (Rev A), (v1.1), (Unl), (Mega Drive 4)
    private static readonly Regex TagPattern = MyRegex();

    internal static string CleanSearchQuery(string fileName)
    {
        var cleanedName = TagPattern.Replace(fileName, "").Trim();

        // If cleaning removed everything (unlikely), fall back to the original name
        return string.IsNullOrWhiteSpace(cleanedName) ? fileName : cleanedName;
    }

    [GeneratedRegex(@"\s*(\(.*?\)|\[.*?\]|\{.*?\})", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
