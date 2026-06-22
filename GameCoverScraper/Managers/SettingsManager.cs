using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Xml.Linq;
using GameCoverScraper.Services;
using MessageBox = System.Windows.MessageBox;

namespace GameCoverScraper.Managers;

public class SettingsManager : INotifyPropertyChanged
{
    public static SettingsManager? CurrentInstance { get; private set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private static readonly string SettingsFilePath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, AppConstants.SettingsFileName);

    private static readonly string UserDataSettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameCoverScraper", AppConstants.SettingsFileName);

    private readonly object _saveLock = new();

    // --- Similarity settings (from FindRomCover) ---

    private double _similarityThreshold;

    public double SimilarityThreshold
    {
        get => _similarityThreshold;
        set
        {
            value = Math.Clamp(value, 0, 100);
            if (Math.Abs(_similarityThreshold - value) < 0.01) return;

            _similarityThreshold = value;
            OnPropertyChanged(nameof(SimilarityThreshold));
        }
    }

    private string _selectedSimilarityAlgorithm = string.Empty;

    public string SelectedSimilarityAlgorithm
    {
        get => _selectedSimilarityAlgorithm;
        set
        {
            if (_selectedSimilarityAlgorithm == value) return;

            _selectedSimilarityAlgorithm = value;
            OnPropertyChanged(nameof(SelectedSimilarityAlgorithm));
        }
    }

    private int _maxImagesToLoad = 30;

    public int MaxImagesToLoad
    {
        get => _maxImagesToLoad;
        set
        {
            value = Math.Clamp(value, 1, 1000);
            if (_maxImagesToLoad == value) return;

            _maxImagesToLoad = value;
            OnPropertyChanged(nameof(MaxImagesToLoad));
        }
    }

    private int _imageLoaderMaxRetries = 3;

    public int ImageLoaderMaxRetries
    {
        get => _imageLoaderMaxRetries;
        set
        {
            value = Math.Clamp(value, 0, 20);
            if (_imageLoaderMaxRetries == value) return;

            _imageLoaderMaxRetries = value;
            OnPropertyChanged(nameof(ImageLoaderMaxRetries));
        }
    }

    private int _imageLoaderRetryDelayMilliseconds = 200;

    public int ImageLoaderRetryDelayMilliseconds
    {
        get => _imageLoaderRetryDelayMilliseconds;
        set
        {
            value = Math.Clamp(value, 0, 10000);
            if (_imageLoaderRetryDelayMilliseconds == value) return;

            _imageLoaderRetryDelayMilliseconds = value;
            OnPropertyChanged(nameof(ImageLoaderRetryDelayMilliseconds));
        }
    }

    private int _apiTimeoutSeconds = 30;

    public int ApiTimeoutSeconds
    {
        get => _apiTimeoutSeconds;
        set
        {
            value = Math.Clamp(value, 1, 300);
            if (_apiTimeoutSeconds == value) return;

            _apiTimeoutSeconds = value;
            OnPropertyChanged(nameof(ApiTimeoutSeconds));
        }
    }

    private string _lastImageFolder = string.Empty;

    public string LastImageFolder
    {
        get => _lastImageFolder;
        set
        {
            if (_lastImageFolder == value) return;

            _lastImageFolder = value;
            OnPropertyChanged(nameof(LastImageFolder));
        }
    }

    // --- Thumbnail settings (merged: ImageWidth/ImageHeight from FindRomCover, ThumbnailSize from GameCoverScraper) ---

    private int _imageWidth = 300;

    public int ImageWidth
    {
        get => _imageWidth;
        set
        {
            value = Math.Clamp(value, 50, 2000);
            if (_imageWidth == value) return;

            _imageWidth = value;
            OnPropertyChanged(nameof(ImageWidth));
        }
    }

    private int _imageHeight = 300;

    public int ImageHeight
    {
        get => _imageHeight;
        set
        {
            value = Math.Clamp(value, 50, 2000);
            if (_imageHeight == value) return;

            _imageHeight = value;
            OnPropertyChanged(nameof(ImageHeight));
        }
    }

    public int ThumbnailSize
    {
        get => _imageWidth;
        set
        {
            ImageWidth = value;
            ImageHeight = value;
        }
    }

    // --- Theme settings ---

    private static readonly HashSet<string> ValidBaseThemes = new(StringComparer.OrdinalIgnoreCase) { "Light", "Dark" };

    private string _baseTheme = "Dark";

    public string BaseTheme
    {
        get => _baseTheme;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !ValidBaseThemes.Contains(value))
            {
                value = "Dark";
            }

            if (_baseTheme == value) return;

            _baseTheme = value;
            OnPropertyChanged(nameof(BaseTheme));
        }
    }

    private static readonly HashSet<string> ValidAccentColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "Red", "Green", "Blue", "Orange", "Purple", "Pink", "Lime", "Emerald",
        "Teal", "Cyan", "Cobalt", "Indigo", "Violet", "Magenta", "Crimson",
        "Amber", "Yellow", "Brown", "Olive", "Steel", "Mauve", "Taupe", "Sienna"
    };

    private string _accentColor = "Blue";

    public string AccentColor
    {
        get => _accentColor;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || !ValidAccentColors.Contains(value))
            {
                value = "Blue";
            }

            if (_accentColor == value) return;

            _accentColor = value;
            OnPropertyChanged(nameof(AccentColor));
        }
    }

    // --- MAME settings ---

    private bool _useMameDescriptions;

    public bool UseMameDescriptions
    {
        get => _useMameDescriptions;
        set
        {
            if (_useMameDescriptions == value) return;

            _useMameDescriptions = value;
            OnPropertyChanged(nameof(UseMameDescriptions));
        }
    }

    // --- Extensions ---

    private string[] _supportedExtensions = Array.Empty<string>();

    public string[] SupportedExtensions
    {
        get => _supportedExtensions;
        set
        {
            if (_supportedExtensions.SequenceEqual(value)) return;

            _supportedExtensions = value;
            OnPropertyChanged(nameof(SupportedExtensions));
        }
    }

    // --- GameCoverScraper-specific settings ---

    private string _searchEngine = "BingWeb";

    public string SearchEngine
    {
        get => _searchEngine;
        set
        {
            if (_searchEngine == value) return;

            _searchEngine = value;
            OnPropertyChanged(nameof(SearchEngine));
        }
    }

    private string _bugReportApiKey = AppConstants.BugReportApiKey;

    public string BugReportApiKey
    {
        get => _bugReportApiKey;
        set
        {
            if (_bugReportApiKey == value) return;

            _bugReportApiKey = value;
            OnPropertyChanged(nameof(BugReportApiKey));
        }
    }

    private string _bugReportApiUrl = AppConstants.BugReportApiUrl;

    public string BugReportApiUrl
    {
        get => _bugReportApiUrl;
        set
        {
            if (_bugReportApiUrl == value) return;

            _bugReportApiUrl = value;
            OnPropertyChanged(nameof(BugReportApiUrl));
        }
    }

    private string _googleKey = string.Empty;

    public string GoogleKey
    {
        get => _googleKey;
        set
        {
            if (_googleKey == value) return;

            _googleKey = value;
            OnPropertyChanged(nameof(GoogleKey));
        }
    }

    private string _googleSearchEngineId = "d30e97188f5914611";

    public string GoogleSearchEngineId
    {
        get => _googleSearchEngineId;
        set
        {
            if (_googleSearchEngineId == value) return;

            _googleSearchEngineId = value;
            OnPropertyChanged(nameof(GoogleSearchEngineId));
        }
    }

    // --- Constructor and Load/Save ---

    public SettingsManager()
    {
        CurrentInstance = this;
        LoadSettings();
    }

    public void LoadSettings()
    {
        try
        {
            var bestPath = GetMostRecentSettingsFilePath();

            if (bestPath == null || !File.Exists(bestPath))
            {
                SetDefaultSettings();
                try { SaveSettings(); }
                catch (Exception saveEx) { _ = ErrorLogger.LogAsync(saveEx, "Failed to save default settings to settings.xml"); }

                return;
            }

            var doc = XDocument.Load(bestPath);
            var root = doc.Element("Settings");

            if (root == null)
            {
                throw new InvalidDataException("The settings.xml file is missing the root <Settings> element.");
            }

            string GetValue(string elementName, string defaultValue)
            {
                return root.Element(elementName)?.Value ?? defaultValue;
            }

            SimilarityThreshold = double.Parse(GetValue("SimilarityThreshold", "70"), CultureInfo.InvariantCulture);
            SelectedSimilarityAlgorithm = GetValue("SimilarityAlgorithm", "Jaro-Winkler Distance");
            BaseTheme = GetValue("BaseTheme", "Dark");
            AccentColor = GetValue("AccentColor", "Blue");

            var imageSizeElement = root.Element("ImageSize");
            if (imageSizeElement != null)
            {
                ImageWidth = int.Parse(imageSizeElement.Element("Width")?.Value ?? "300", CultureInfo.InvariantCulture);
                ImageHeight = int.Parse(imageSizeElement.Element("Height")?.Value ?? "300", CultureInfo.InvariantCulture);
            }
            else
            {
                var parsedSize = int.Parse(GetValue("ThumbnailSize", "300"), CultureInfo.InvariantCulture);
                ImageWidth = parsedSize;
                ImageHeight = parsedSize;
            }

            MaxImagesToLoad = int.Parse(GetValue("MaxImagesToLoad", "30"), CultureInfo.InvariantCulture);
            ImageLoaderMaxRetries = int.Parse(GetValue("ImageLoaderMaxRetries", "3"), CultureInfo.InvariantCulture);
            ImageLoaderRetryDelayMilliseconds = int.Parse(GetValue("ImageLoaderRetryDelayMilliseconds", "200"), CultureInfo.InvariantCulture);
            ApiTimeoutSeconds = int.Parse(GetValue("ApiTimeoutSeconds", "30"), CultureInfo.InvariantCulture);

            SearchEngine = GetValue("SearchEngine", "BingWeb");
            BugReportApiKey = GetValue("BugReportApiKey", AppConstants.BugReportApiKey);
            BugReportApiUrl = GetValue("BugReportApiUrl", AppConstants.BugReportApiUrl);
            GoogleKey = GetValue("GoogleKey", string.Empty);
            GoogleSearchEngineId = GetValue("GoogleSearchEngineId", "d30e97188f5914611");

            var extensionsElement = root.Element("SupportedExtensions");
            if (extensionsElement != null)
            {
                SupportedExtensions = extensionsElement.Elements("Extension")
                    .Select(static e => e.Value)
                    .Where(static e => !string.IsNullOrEmpty(e))
                    .ToArray();
            }

            if (SupportedExtensions.Length == 0)
            {
                SupportedExtensions = GetDefaultExtensions();
            }

            var useMameDescValue = GetValue("UseMameDescriptions", GetValue("UseMameDescription", "false"));
            UseMameDescriptions = string.Equals(useMameDescValue, "true", StringComparison.OrdinalIgnoreCase);

            LastImageFolder = GetValue("LastImageFolder", string.Empty);
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error loading settings from settings.xml");
            SetDefaultSettings();
            try { SaveSettings(); }
            catch (Exception saveEx) { _ = ErrorLogger.LogAsync(saveEx, "Failed to save default settings after load error"); }
        }
    }

    private static string? GetMostRecentSettingsFilePath()
    {
        var appDirExists = File.Exists(SettingsFilePath);
        var userDataExists = File.Exists(UserDataSettingsFilePath);

        switch (appDirExists)
        {
            case false when !userDataExists:
                return null;
            case true when !userDataExists:
                return SettingsFilePath;
            case false when userDataExists:
                return UserDataSettingsFilePath;
            default:
                // Both exist - use the most recently modified one
                try
                {
                    var appDirTime = File.GetLastWriteTimeUtc(SettingsFilePath);
                    var userDataTime = File.GetLastWriteTimeUtc(UserDataSettingsFilePath);
                    return userDataTime > appDirTime ? UserDataSettingsFilePath : SettingsFilePath;
                }
                catch
                {
                    // If we can't get the write time, prefer the app directory version
                    return SettingsFilePath;
                }
        }
    }

    public void SaveSettings()
    {
        lock (_saveLock)
        {
            var doc = new XDocument(
                new XElement("Settings",
                    new XElement("SimilarityThreshold", SimilarityThreshold.ToString(CultureInfo.InvariantCulture)),
                    new XElement("SimilarityAlgorithm", SelectedSimilarityAlgorithm),
                    new XElement("SupportedExtensions",
                        SupportedExtensions.Select(static ext => new XElement("Extension", ext))
                    ),
                    new XElement("ImageSize",
                        new XElement("Width", ImageWidth),
                        new XElement("Height", ImageHeight)
                    ),
                    new XElement("MaxImagesToLoad", MaxImagesToLoad),
                    new XElement("ImageLoaderMaxRetries", ImageLoaderMaxRetries),
                    new XElement("ImageLoaderRetryDelayMilliseconds", ImageLoaderRetryDelayMilliseconds),
                    new XElement("ApiTimeoutSeconds", ApiTimeoutSeconds),
                    new XElement("BaseTheme", BaseTheme),
                    new XElement("AccentColor", AccentColor),
                    new XElement("UseMameDescriptions", UseMameDescriptions.ToString().ToLowerInvariant()),
                    new XElement("LastImageFolder", LastImageFolder),
                    new XElement("SearchEngine", SearchEngine),
                    new XElement("BugReportApiKey", BugReportApiKey),
                    new XElement("BugReportApiUrl", BugReportApiUrl),
                    new XElement("GoogleKey", GoogleKey),
                    new XElement("GoogleSearchEngineId", GoogleSearchEngineId)
                )
            );

            // Try to save to the application directory first
            var savedToAppDir = TrySaveToFile(doc, SettingsFilePath);

            // Also save to the user data folder as a backup / fallback
            _ = TrySaveToFile(doc, UserDataSettingsFilePath);

            if (!savedToAppDir)
            {
                ShowSaveError("Could not save settings to the application folder. Settings were saved to the user data folder instead.");
            }
        }
    }

    private bool TrySaveToFile(XDocument doc, string filePath)
    {
        var tempFilePath = filePath + ".tmp";
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            doc.Save(tempFilePath);
            File.Copy(tempFilePath, filePath, true);
            return true;
        }
        catch (UnauthorizedAccessException ex)
        {
            _ = ErrorLogger.LogAsync(ex, $"Access denied saving settings to: {filePath}");
            return false;
        }
        catch (IOException ex)
        {
            _ = ErrorLogger.LogAsync(ex, $"I/O error saving settings to: {filePath}");
            return false;
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, $"Failed to save settings to: {filePath}");
            return false;
        }
        finally
        {
            try { if (File.Exists(tempFilePath)) File.Delete(tempFilePath); }
            catch (Exception cleanupEx) { _ = ErrorLogger.LogAsync(cleanupEx, $"Failed to cleanup settings temp file: {tempFilePath}"); }
        }
    }

    private static void ShowSaveError(string message)
    {
        if (Application.Current != null)
        {
            MessageBox.Show(message, "Settings Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        else
        {
            _ = ErrorLogger.LogAsync(new InvalidOperationException(message), "Settings save error (no UI available)");
        }
    }

    private void SetDefaultSettings()
    {
        _similarityThreshold = double.Parse(AppConstants.Messages.DefaultSimilarityThreshold, CultureInfo.InvariantCulture);
        _selectedSimilarityAlgorithm = AppConstants.Algorithms.JaroWinkler;
        _supportedExtensions = GetDefaultExtensions();
        _imageWidth = 300;
        _imageHeight = 300;
        _baseTheme = AppConstants.Themes.Dark;
        _accentColor = "Blue";
        _useMameDescriptions = false;
        _lastImageFolder = string.Empty;
        _searchEngine = "BingWeb";
        _bugReportApiKey = AppConstants.BugReportApiKey;
        _bugReportApiUrl = AppConstants.BugReportApiUrl;
        _googleKey = string.Empty;
        _googleSearchEngineId = "d30e97188f5914611";
    }

    private static string[] GetDefaultExtensions()
    {
        return
        [
            "2hd", "3ds", "7z", "88d", "a78", "arc", "bat", "bin", "bs", "cas", "ccd", "cdi", "cdt", "chd", "cht",
            "ciso", "cmd", "col", "cpr", "cso", "cue", "cv", "d64", "d71", "d81", "d88", "dim", "dol", "dsk", "dup",
            "elf", "exe", "fdi", "fds", "fig", "g64", "gb", "gcm", "gcz", "gdi", "gg", "gz", "hdf", "hdm", "img",
            "int", "ipf", "iso", "lnk", "lnx", "m3u", "mdf", "mds", "ms1", "msa", "mx1", "mx2", "n64", "nbz", "nca",
            "ndd", "nds", "nes", "nib", "nrg", "nro", "nso", "nsp", "o", "pbp", "pce", "prg", "prx", "rar", "ri",
            "rom", "rvz", "sc", "scl", "sda", "sf", "sfc", "sfx", "sg", "smc", "sms", "sna", "st", "stx", "swc",
            "t64", "tap", "tgc", "toc", "trd", "tzx", "u1", "unf", "unif", "v64", "voc", "wad", "wbfs", "wua", "xci",
            "xdf", "z64", "z80", "zip", "zso",
            "gba", "gbc", "snes", "smc", "md", "smd", "gen", "32x", "sgg"
        ];
    }
}
