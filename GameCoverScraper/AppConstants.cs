namespace GameCoverScraper;

public static class AppConstants
{
    public const string MameDatFileName = "mame.dat";
    public const string SettingsFileName = "settings.xml";

    public const long DefaultMemoryLimit = 512L * 1024 * 1024;
    public const int DefaultThreadLimit = 4;

    public static class Themes
    {
        public const string Light = "Light";
        public const string Dark = "Dark";
    }

    public static class Algorithms
    {
        public const string JaroWinkler = "Jaro-Winkler Distance";
        public const string Jaccard = "Jaccard Similarity";
        public const string Levenshtein = "Levenshtein Distance";
    }

    public static class Messages
    {
        public const string DefaultSimilarityThreshold = "70";
        public const string MissingCoversPrefix = "MISSING COVERS: ";
    }
}
