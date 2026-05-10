using System.Globalization;
using System.IO;
using System.Xml.Linq;
using GameCoverScraper.Services;

namespace GameCoverScraper.Managers;

public class SettingsManager
{
    private static readonly object SettingsLock = new();
    private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
    private int _thumbnailSize = DefaultThumbnailSize;
    private const int DefaultThumbnailSize = 300;
    private const int MinThumbnailSize = 50;
    private const int MaxThumbnailSize = 800;

    public int ThumbnailSize
    {
        get
        {
            lock (SettingsLock)
            {
                return _thumbnailSize;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                if (value is < MinThumbnailSize or > MaxThumbnailSize)
                    throw new ArgumentOutOfRangeException(nameof(value), $"Thumbnail size must be between {MinThumbnailSize} and {MaxThumbnailSize}.");

                _thumbnailSize = value;
            }
        }
    }

    public string SearchEngine
    {
        get
        {
            lock (SettingsLock)
            {
                return _searchEngine;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _searchEngine = value;
            }
        }
    }

    private string _searchEngine = DefaultSearchEngine;
    private const string DefaultSearchEngine = "BingWeb";

    public string BaseTheme
    {
        get
        {
            lock (SettingsLock)
            {
                return _baseTheme;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _baseTheme = value;
            }
        }
    }

    private string _baseTheme = DefaultBaseTheme;
    private const string DefaultBaseTheme = "Light";

    public string AccentColor
    {
        get
        {
            lock (SettingsLock)
            {
                return _accentColor;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _accentColor = value;
            }
        }
    }

    private string _accentColor = DefaultAccentColor;
    private const string DefaultAccentColor = "Blue";

    public bool UseMameDescriptions
    {
        get
        {
            lock (SettingsLock)
            {
                return _useMameDescriptions;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _useMameDescriptions = value;
            }
        }
    }

    private bool _useMameDescriptions;

    public List<string> SupportedExtensions
    {
        get
        {
            lock (SettingsLock)
            {
                return _supportedExtensions;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _supportedExtensions = value;
            }
        }
    }

    private List<string> _supportedExtensions = new();

    public string BugReportApiKey
    {
        get
        {
            lock (SettingsLock)
            {
                return _bugReportApiKey;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _bugReportApiKey = value;
            }
        }
    }

    private string _bugReportApiKey = DefaultBugReportApiKey;
    private const string DefaultBugReportApiKey = "hjh7yu6t56tyr540o9u8767676r5674534453235264c75b6t7ggghgg76trf564e";

    public string BugReportApiUrl
    {
        get
        {
            lock (SettingsLock)
            {
                return _bugReportApiUrl;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _bugReportApiUrl = value;
            }
        }
    }

    private string _bugReportApiUrl = DefaultBugReportApiUrl;
    private const string DefaultBugReportApiUrl = "https://www.purelogiccode.com/bugreport/api/send-bug-report";

    private static readonly List<string> DefaultSupportedExtensions =
    [
        "zip", "rar", "7z", "gba", "gb", "gbc", "nes", "snes", "sfc", "smc",
        "md", "smd", "gen", "32x", "sgg", "sg", "sc", "ms", "gg", "rom", "bin"
    ];

    public string GoogleKey
    {
        get
        {
            lock (SettingsLock)
            {
                return _googleKey;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _googleKey = value;
            }
        }
    }

    private string _googleKey = string.Empty;

    public string GoogleSearchEngineId
    {
        get
        {
            lock (SettingsLock)
            {
                return _googleSearchEngineId;
            }
        }
        set
        {
            lock (SettingsLock)
            {
                _googleSearchEngineId = value;
            }
        }
    }

    private string _googleSearchEngineId = "d30e97188f5914611";

    public void LoadSettings()
    {
        lock (SettingsLock)
        {
            AppLogger.Log("Loading settings from settings.xml.");
            if (!File.Exists(SettingsFilePath))
            {
                AppLogger.Log("settings.xml not found. Creating and saving default settings.");
                _supportedExtensions = new List<string>(DefaultSupportedExtensions);
                SaveSettings();
                return;
            }

            try
            {
                var doc = XDocument.Load(SettingsFilePath);
                var root = doc.Element("Settings") ?? throw new InvalidOperationException("Invalid settings file format.");

                var parsedSize = int.Parse(root.Element("ThumbnailSize")?.Value ?? DefaultThumbnailSize.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
                _thumbnailSize = parsedSize is >= MinThumbnailSize and <= MaxThumbnailSize ? parsedSize : DefaultThumbnailSize;
                _searchEngine = root.Element("SearchEngine")?.Value ?? DefaultSearchEngine;
                _baseTheme = root.Element("BaseTheme")?.Value ?? DefaultBaseTheme;
                _accentColor = root.Element("AccentColor")?.Value ?? DefaultAccentColor;
                _useMameDescriptions = bool.Parse(root.Element("UseMameDescriptions")?.Value ?? "false");
                _bugReportApiKey = root.Element("BugReportApiKey")?.Value ?? DefaultBugReportApiKey;
                _bugReportApiUrl = root.Element("BugReportApiUrl")?.Value ?? DefaultBugReportApiUrl;
                _supportedExtensions = root.Element("SupportedExtensions")?
                    .Elements("Extension")
                    .Select(static x => x.Value)
                    .ToList() ?? new List<string>();
                _googleKey = root.Element("GoogleKey")?.Value ?? string.Empty;
                _googleSearchEngineId = root.Element("GoogleSearchEngineId")?.Value ?? "d30e97188f5914611";

                AppLogger.Log("Settings loaded successfully.");
                if (_supportedExtensions.Count != 0)
                {
                    return;
                }

                AppLogger.Log("Supported extensions list was empty, populating with defaults.");
                _supportedExtensions = new List<string>(DefaultSupportedExtensions);

                SaveSettings();
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error loading settings.xml. Reverting to defaults. Error: {ex.Message}");
                _ = BugReport.LogErrorAsync(ex, "Error loading settings.xml. Reverting to defaults.");

                _thumbnailSize = DefaultThumbnailSize;
                _searchEngine = DefaultSearchEngine;
                _baseTheme = DefaultBaseTheme;
                _accentColor = DefaultAccentColor;
                _useMameDescriptions = false;
                _bugReportApiKey = DefaultBugReportApiKey;
                _bugReportApiUrl = DefaultBugReportApiUrl;
                _supportedExtensions = new List<string>(DefaultSupportedExtensions);
                _googleKey = string.Empty;
                _googleSearchEngineId = "d30e97188f5914611";

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
                    new XElement("ThumbnailSize", _thumbnailSize),
                    new XElement("SearchEngine", _searchEngine),
                    new XElement("BaseTheme", _baseTheme),
                    new XElement("AccentColor", _accentColor),
                    new XElement("UseMameDescriptions", _useMameDescriptions),
                    new XElement("BugReportApiKey", _bugReportApiKey),
                    new XElement("BugReportApiUrl", _bugReportApiUrl),
                    new XElement("SupportedExtensions", _supportedExtensions.Select(static ext => new XElement("Extension", ext))),
                    new XElement("GoogleKey", _googleKey),
                    new XElement("GoogleSearchEngineId", _googleSearchEngineId)
                )
            );

            try
            {
                // Use FileStream with FileShare.None to prevent concurrent access issues
                using var fileStream = new FileStream(SettingsFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                doc.Save(fileStream);
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