using System.IO;
using System.Text.RegularExpressions;

namespace GameCoverScraper.Services;

internal static partial class SearchQueryHelper
{
    // Matches common patterns in parentheses or square brackets.
    // e.g., (USA), (Europe), (Japan), (Brazil), (En,Ja), [!], (Rev A), (v1.1), (Unl), (Mega Drive 4)
    private static readonly Regex TagPattern = MyRegex();

    /// <summary>
    /// Cleans a ROM filename to create a better web search query.
    /// Removes common emulator tags like (USA), [!], (Rev A), etc.
    /// </summary>
    /// <param name="fileName">The original filename.</param>
    /// <returns>A cleaner string for searching.</returns>
    internal static string CleanSearchQuery(string fileName)
    {
        var cleanedName = TagPattern.Replace(fileName, "").Trim();

        // If cleaning removed everything (unlikely), fall back to the original name
        return string.IsNullOrWhiteSpace(cleanedName) ? fileName : cleanedName;
    }

    /// <summary>
    /// Sanitizes a filename to prevent path traversal attacks.
    /// Removes path separator characters and other potentially dangerous characters.
    /// </summary>
    /// <param name="fileName">The original filename.</param>
    /// <returns>A sanitized filename safe for use in path construction.</returns>
    internal static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "unnamed";
        }

        // Remove path traversal sequences and invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();

        // Iteratively remove ".." to prevent traversal attacks (e.g., "...." -> "..")
        var sanitized = fileName;
        while (sanitized.Contains(".."))
        {
            sanitized = sanitized.Replace("..", "");
        }

        sanitized = new string(sanitized
            .Replace("/", "")
            .Replace("\\", "")
            .Select(c => invalidChars.Contains(c) ? '_' : c)
            .ToArray());

        // Trim whitespace and dots from ends
        sanitized = sanitized.Trim().TrimEnd('.');

        // Ensure we have something left
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    [GeneratedRegex(@"\s*(\(.*?\)|\[.*?\]|\{.*?\})", RegexOptions.Compiled)]
    private static partial Regex MyRegex();
}
