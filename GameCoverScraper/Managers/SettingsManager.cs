using System.Globalization;
using System.IO;
using System.Xml.Linq;
using GameCoverScraper.Services;

namespace GameCoverScraper.Managers;

public class SettingsManager
{
    private static readonly Lock Lock = new();

    private static readonly object SettingsLock = new();
    private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
    public int ThumbnailSize { get; set; } = DefaultThumbnailSize;
    private const int DefaultThumbnailSize = 300;
    public string SearchEngine { get; set; } = DefaultSearchEngine;
    private const string DefaultSearchEngine = "GoogleWeb";
    public string BaseTheme { get; set; } = DefaultBaseTheme;
    private const string DefaultBaseTheme = "Light";
    public string AccentColor { get; set; } = DefaultAccentColor;
    private const string DefaultAccentColor = "Blue";
    public bool UseMameDescriptions { get; set; }
    public List<string> SupportedExtensions { get; private set; } = new();

    public string BugReportApiKey { get; set; } = DefaultBugReportApiKey;
    private const string DefaultBugReportApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";
    public string BugReportApiUrl { get; set; } = DefaultBugReportApiUrl;
    private const string DefaultBugReportApiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";

    private static readonly List<string> DefaultSupportedExtensions = new()
    {
        "zip", "rar", "7z", "gba", "gb", "gbc", "nes", "snes", "sfc", "smc",
        "md", "smd", "gen", "32x", "sgg", "sg", "sc", "ms", "gg", "rom", "bin"
    };

    public string GoogleKey { get; set; } = string.Empty;
    public string GoogleSearchEngineId { get; private set; } = "d30e97188f5914611";

    public void LoadSettings()
    {
        lock (SettingsLock)
        {
            AppLogger.Log("Loading settings from settings.xml.");
            if (!File.Exists(SettingsFilePath))
            {
                AppLogger.Log("settings.xml not found. Creating and saving default settings.");
                SupportedExtensions = new List<string>(DefaultSupportedExtensions);
                SaveSettings();
                return;
            }

            try
            {
                var doc = XDocument.Load(SettingsFilePath);
                var root = doc.Element("Settings") ?? throw new InvalidOperationException("Invalid settings file format.");

                ThumbnailSize = int.Parse(root.Element("ThumbnailSize")?.Value ?? DefaultThumbnailSize.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                SearchEngine = root.Element("SearchEngine")?.Value ?? DefaultSearchEngine;
                BaseTheme = root.Element("BaseTheme")?.Value ?? DefaultBaseTheme;
                AccentColor = root.Element("AccentColor")?.Value ?? DefaultAccentColor;
                UseMameDescriptions = bool.Parse(root.Element("UseMameDescriptions")?.Value ?? "false");
                BugReportApiKey = root.Element("BugReportApiKey")?.Value ?? DefaultBugReportApiKey;
                BugReportApiUrl = root.Element("BugReportApiUrl")?.Value ?? DefaultBugReportApiUrl;
                SupportedExtensions = root.Element("SupportedExtensions")?
                    .Elements("Extension")
                    .Select(static x => x.Value)
                    .ToList() ?? new List<string>();
                GoogleKey = root.Element("GoogleKey")?.Value ?? string.Empty;
                GoogleSearchEngineId = root.Element("GoogleSearchEngineId")?.Value ?? "d30e97188f5914611";

                AppLogger.Log("Settings loaded successfully.");
                if (SupportedExtensions.Count != 0)
                {
                    return;
                }

                AppLogger.Log("Supported extensions list was empty, populating with defaults.");
                SupportedExtensions = new List<string>(DefaultSupportedExtensions);

                SaveSettings();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error loading settings.xml. Reverting to defaults. Error: {ex.Message}");
                _ = BugReport.LogErrorAsync(ex, "Error loading settings.xml. Reverting to defaults.");

                ThumbnailSize = DefaultThumbnailSize;
                SearchEngine = DefaultSearchEngine;
                BaseTheme = DefaultBaseTheme;
                AccentColor = DefaultAccentColor;
                UseMameDescriptions = true;
                BugReportApiKey = DefaultBugReportApiKey;
                BugReportApiUrl = DefaultBugReportApiUrl;
                SupportedExtensions = new List<string>(DefaultSupportedExtensions);
                GoogleKey = string.Empty;
                GoogleSearchEngineId = "d30e97188f5914611";

                SaveSettings();
            }
        }
    }

    public void SaveSettings()
    {
        lock (SettingsLock)
        {
            AppLogger.Log("Saving settings to settings.xml.");
            var doc = new XDocument(
                new XElement("Settings",
                    new XElement("ThumbnailSize", ThumbnailSize),
                    new XElement("SearchEngine", SearchEngine),
                    new XElement("BaseTheme", BaseTheme),
                    new XElement("AccentColor", AccentColor),
                    new XElement("UseMameDescriptions", UseMameDescriptions),
                    new XElement("BugReportApiKey", BugReportApiKey),
                    new XElement("BugReportApiUrl", BugReportApiUrl),
                    new XElement("SupportedExtensions", SupportedExtensions.Select(static ext => new XElement("Extension", ext))),
                    new XElement("GoogleKey", GoogleKey),
                    new XElement("GoogleSearchEngineId", GoogleSearchEngineId)
                )
            );

            try
            {
                doc.Save(SettingsFilePath);
                AppLogger.Log("Settings saved successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error saving settings.xml. Error: {ex.Message}");
                _ = BugReport.LogErrorAsync(ex, "Error saving settings.xml.");
            }
        }
    }
}