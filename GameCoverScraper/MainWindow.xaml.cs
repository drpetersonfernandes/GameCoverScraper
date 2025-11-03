using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ControlzEx.Theming;
using Microsoft.Win32;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Input;
using GameCoverScraper.ApiProvider;
using GameCoverScraper.Managers;
using GameCoverScraper.models;
using GameCoverScraper.Services;
using Microsoft.Web.WebView2.Core;
using SixLabors.ImageSharp;
using Image = SixLabors.ImageSharp.Image;
using System.Diagnostics;

namespace GameCoverScraper;

public partial class MainWindow : INotifyPropertyChanged, IDisposable
{
    private readonly List<MameManager> _machines;
    private readonly Dictionary<string, string> _mameLookup;
    private FileSystemWatcher? _imageFolderWatcher;

    private string? _imageFolderPath;
    private string? _selectedGameFileName;
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _selectionDelayTimer;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ImageData> PanelImages { get; set; }

    private bool _isSearching;

    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            _isSearching = value;
            OnPropertyChanged();
        }
    }

    private readonly SettingsManager _settingsManager;

    private string _searchEngineDisplay = string.Empty;

    public string SearchEngineDisplay
    {
        get => _searchEngineDisplay;
        set
        {
            _searchEngineDisplay = value;
            OnPropertyChanged();
        }
    }

    private string _statusMessageText = "Ready";

    public MainWindow(string? startupImageFolder = null, string? startupRomFolder = null) // Modified constructor
    {
        InitializeComponent();
        AppLogger.Log("MainWindow initializing...");
        DataContext = this;
        PanelImages = new ObservableCollection<ImageData>();
        TxtExtraQuery.Text = "";

        // Initialize the status bar
        PanelImages.CollectionChanged += (s, e) =>
        {
            if (Dispatcher.CheckAccess())
            {
                UpdateStatusBar();
            }
            else
            {
                Dispatcher.Invoke(UpdateStatusBar);
            }
        };

        // Initialize the selection delay timer
        _selectionDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _selectionDelayTimer.Tick += SelectionDelayTimer_Tick;
        AppLogger.Log("Selection delay timer initialized.");

        _settingsManager = new SettingsManager();
        _settingsManager.LoadSettings();

        SetCheckedStateForThemeAndAccent();
        UpdateCheckedState();
        UpdateMenuItems();

        Closing += MainWindow_Closing;
        Closed += (s, e) => Dispose();

        // Subscribe to the debug window hidden event if it exists
        if (App.LogWindow != null)
        {
            App.LogWindow.WindowHidden -= OnDebugWindowHidden;
            App.LogWindow.WindowHidden += OnDebugWindowHidden;
        }

        AppLogger.Log("MainWindow initialized.");

        // Initialize _machines and _mameLookup to empty collections by default
        _machines = new List<MameManager>();
        _mameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Load _machines and _mameLookup with error handling
        try
        {
            _machines = MameManager.LoadFromDat();
            _mameLookup = _machines
                .GroupBy(static m => m.MachineName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.First().Description, StringComparer.OrdinalIgnoreCase);
            AppLogger.Log($"Successfully loaded {_machines.Count} MAME entries.");
        }
        catch (MameDatNotFoundException ex)
        {
            AppLogger.Log($"MAME data file not found: {ex.Message}");
            MessageBox.Show("The required data file 'mame.dat' was not found.\n\n" +
                            $"Please ensure the file is placed in the application directory: {AppDomain.CurrentDomain.BaseDirectory}\n\n" +
                            "If you have not moved the application files, please try reinstalling the GameCoverScraper application.\n\n" +
                            "MAME descriptions will not be available.", "Missing MAME Data File", MessageBoxButton.OK, MessageBoxImage.Warning);
            _ = BugReport.LogErrorAsync(ex, "The file 'mame.dat' could not be found in the application folder.");
        }
        catch (MameDatCorruptError ex)
        {
            AppLogger.Log($"MAME data file corrupted: {ex.Message}");
            MessageBox.Show(
                "The data file 'mame.dat' appears to be corrupted or in an invalid format.\n\n" +
                "Please verify the file integrity or obtain a fresh copy.\n\n" +
                "MAME descriptions will not be available.",
                "Corrupted MAME Data File", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (IOException ex)
        {
            AppLogger.Log($"Error accessing MAME data file: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Error accessing mame.dat file during startup.");
            MessageBox.Show(
                "Unable to access the 'mame.dat' file.\n\n" +
                "Please ensure the file is not being used by another application and try again.\n\n" +
                "MAME descriptions will not be available.",
                "MAME File Access Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Unexpected error loading MAME data: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Unexpected error loading mame.dat during startup.");
            MessageBox.Show(
                "An unexpected error occurred while loading the MAME game data.\n\n" +
                "Please try restarting the application. If the problem persists, " +
                "contact support with the error details.\n\n" +
                "MAME descriptions will not be available.",
                "MAME Data Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        // Add a keyboard event handler
        LstMissingImages.PreviewKeyDown += LstMissingImages_PreviewKeyDown;

        // Add a context menu opening handler for LstMissingImages
        LstMissingImages.ContextMenuOpening += LstMissingImages_ContextMenuOpening;

        ToggleMameDescriptions.IsChecked = _settingsManager.UseMameDescriptions;

        InitializeWebViewAsync();

        // --- Handle startup arguments ---
        if (!string.IsNullOrEmpty(startupImageFolder))
        {
            TxtImageFolder.Text = startupImageFolder;
            _imageFolderPath = startupImageFolder; // Also set the internal field
            AppLogger.Log($"Image folder set from startup argument: '{startupImageFolder}'");
        }

        if (!string.IsNullOrEmpty(startupRomFolder))
        {
            TxtRomFolder.Text = startupRomFolder;
            AppLogger.Log($"ROM folder set from startup argument: '{startupRomFolder}'");
        }

        // If both folders are provided, automatically trigger the check for missing images
        if (!string.IsNullOrEmpty(TxtImageFolder.Text) && !string.IsNullOrEmpty(TxtRomFolder.Text))
        {
            AppLogger.Log("Both startup folders provided. Initializing FileSystemWatcher and checking for missing images automatically.");
            InitializeFileSystemWatcher(); // Ensure a watcher is set up for the provided image folder
            // Use Dispatcher.BeginInvoke to ensure the UI is fully rendered before starting heavy operations
            // and to avoid blocking the constructor.
            Dispatcher.BeginInvoke(new Action(void () =>
            {
                try
                {
                    BtnCheckForMissingImages_Click(this, new RoutedEventArgs());
                }
                catch (Exception ex)
                {
                    _ = BugReport.LogErrorAsync(ex, "Error in MainWindow constructor");
                }
            }), DispatcherPriority.Loaded);
        }
        // --- End handle startup arguments ---
    }

    private void InitializeFileSystemWatcher()
    {
        // Dispose of the old watcher if it exists
        _imageFolderWatcher?.Dispose();
        _imageFolderWatcher = null;

        if (string.IsNullOrEmpty(_imageFolderPath) || !Directory.Exists(_imageFolderPath))
        {
            return;
        }

        AppLogger.Log($"Initializing FileSystemWatcher for path: '{_imageFolderPath}'");
        _imageFolderWatcher = new FileSystemWatcher(_imageFolderPath)
        {
            // Watch for file creation. We can add more filters if needed.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
            // We don't need to watch subdirectories
            IncludeSubdirectories = false
        };

        // Add the event handler
        _imageFolderWatcher.Created += OnImageFileCreated;

        // Start watching
        _imageFolderWatcher.EnableRaisingEvents = true;
        AppLogger.Log("FileSystemWatcher is now active.");
    }

    private async void OnImageFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            // The event can fire for any file, so we must check if it's for a game in our list.
            if (string.IsNullOrEmpty(e.Name))
            {
                return;
            }

            var newFileNameWithoutExt = Path.GetFileNameWithoutExtension(e.Name);

            // Check if this file corresponds to any item in the missing list.
            // This is more robust than checking only against _selectedGameFileName.
            var isFileInMissingList = false;
            await Dispatcher.InvokeAsync(() =>
            {
                isFileInMissingList = LstMissingImages.Items.Cast<object>()
                    .Any(item => string.Equals(item.ToString(), newFileNameWithoutExt, StringComparison.OrdinalIgnoreCase));
            });

            if (!isFileInMissingList)
            {
                return; // Not a game we are looking for, no action needed.
            }

            AppLogger.Log($"FileSystemWatcher detected new file '{e.Name}' for game '{newFileNameWithoutExt}'.");

            try
            {
                // Wait for the file to be accessible to avoid read/write conflicts.
                // This is crucial as files might be locked briefly after creation/download.
                await WaitForFileAccess(e.FullPath, 10000); // Wait up to 10 seconds.

                var newFileExtension = Path.GetExtension(e.Name).ToLowerInvariant();
                string[] sourceImageExtensions = [".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".avif"];

                // If it's already a PNG, just remove the item from the list.
                if (newFileExtension == ".png")
                {
                    AppLogger.Log($"Detected new PNG file. Removing '{newFileNameWithoutExt}' from list.");
                    RemoveItemFromListByName(newFileNameWithoutExt);
                    return;
                }

                // If it's another supported image format, convert it to PNG.
                if (sourceImageExtensions.Contains(newFileExtension))
                {
                    AppLogger.Log($"Detected new image file '{e.Name}'. Converting to PNG.");

                    if (_imageFolderPath != null)
                    {
                        var targetPngPath = Path.Combine(_imageFolderPath, newFileNameWithoutExt + ".png");

                        // Perform conversion
                        if (await ConvertImageToPng(e.FullPath, targetPngPath).ConfigureAwait(false))
                        {
                            // Delete the original file after a successful conversion
                            try
                            {
                                File.Delete(e.FullPath);
                                AppLogger.Log($"Deleted original file: '{e.FullPath}'");
                            }
                            catch (Exception deleteEx)
                            {
                                AppLogger.Log($"Could not delete original file '{e.FullPath}': {deleteEx.Message}");
                                // Log but continue, as the main goal (PNG creation) was successful.
                            }

                            // Remove from the list since we now have the PNG
                            AppLogger.Log($"Conversion successful. Removing '{newFileNameWithoutExt}' from list.");
                            RemoveItemFromListByName(newFileNameWithoutExt);
                        }
                        else
                        {
                            AppLogger.Log($"Failed to convert image file '{e.FullPath}' to PNG.");
                            await Dispatcher.InvokeAsync(() =>
                            {
                                StatusMessageText = $"Failed to convert image for '{newFileNameWithoutExt}'.";
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Error processing new file '{e.FullPath}': {ex.Message}");
                _ = BugReport.LogErrorAsync(ex, $"Error in OnImageFileCreated for file {e.FullPath}");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show($"Error processing new image file '{e.Name}'", "File Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusMessageText = $"Error processing image for '{newFileNameWithoutExt}'.";
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error in OnImageFileCreated: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Error in OnImageFileCreated");
            MessageBox.Show("Error processing the new image file.", "File Processing Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static async Task WaitForFileAccess(string filePath, int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentDelay = 10; // Start with a small delay (e.g., 10 ms)
        const int maxDelay = 200; // Cap the delay (e.g., 200ms)

        while (stopwatch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            try
            {
                // Attempt to open the file with read access and no sharing.
                // If this succeeds, the file is not locked for reading.
                await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                return; // Success
            }
            catch (IOException)
            {
                // File is locked, wait a bit before retrying.
                await Task.Delay(currentDelay).ConfigureAwait(false);
                // Exponentially increase the delay, up to maxDelay
                currentDelay = Math.Min(currentDelay * 2, maxDelay);
            }
        }

        throw new TimeoutException($"File '{filePath}' remained locked after {timeoutMilliseconds}ms.");
    }

    private void RemoveItemFromListByName(string fileName)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => RemoveItemFromListByName(fileName));
            return;
        }

        var itemToRemove = LstMissingImages.Items.Cast<object>()
            .FirstOrDefault(item => string.Equals(item.ToString(), fileName, StringComparison.OrdinalIgnoreCase));

        if (itemToRemove == null)
        {
            AppLogger.Log($"Could not find item '{fileName}' in the missing items list to remove it.");
            return;
        }

        var wasSelected = LstMissingImages.SelectedItem == itemToRemove;
        var oldIndex = LstMissingImages.Items.IndexOf(itemToRemove);

        LstMissingImages.Items.Remove(itemToRemove);
        AppLogger.Log($"Removed '{fileName}' from the missing items list.");

        // If the removed item was the one selected, auto-select another.
        if (wasSelected)
        {
            if (LstMissingImages.Items.Count > 0)
            {
                // Try to select the item at the same index, or the one before if it was the last.
                var newIndex = Math.Min(oldIndex, LstMissingImages.Items.Count - 1);
                LstMissingImages.SelectedIndex = newIndex;
                LstMissingImages.ScrollIntoView(LstMissingImages.SelectedItem);
            }
            else
            {
                // List is now empty, clear the UI.
                PanelImages.Clear();
                LblSearchQuery.Content = "";
                IsSearching = false;
                StatusMessageText = "All covers found!";
                _selectedGameFileName = null;
                if (WebView?.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.Navigate("about:blank");
                }
            }
        }

        PlaySound.PlayClickSound();
        UpdateMissingImagesCount();
    }

    private async void InitializeWebViewAsync()
    {
        try
        {
            AppLogger.Log("Initializing WebView2...");
            await WebView.EnsureCoreWebView2Async(null);
            WebView.NavigationCompleted += WebView_NavigationCompleted;

            // Ensure right-click menus are enabled (this is the default, but good to be explicit)
            if (WebView.CoreWebView2 != null)
            {
                WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                AppLogger.Log("WebView2 default context menus enabled.");
            }

            AppLogger.Log("WebView2 initialized successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"WebView2 initialization failed: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "WebView2 initialization failed.");

            // WEBVIEW2 DOWNLOAD LINK
            var result = MessageBox.Show("The web browser component (Microsoft Edge WebView2 Runtime) is required but could not be loaded. " +
                                         "This usually means it's not installed or is corrupted.\n\n" +
                                         "Would you like to open the download page for the Microsoft Edge WebView2 Runtime now?", "WebView2 Error", MessageBoxButton.YesNo, MessageBoxImage.Error);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://developer.microsoft.com/en-us/microsoft-edge/webview2/")
                    {
                        UseShellExecute = true
                    });
                    AppLogger.Log("Opened WebView2 Runtime download page for user.");
                }
                catch (Exception linkEx)
                {
                    AppLogger.Log($"Failed to open WebView2 download link: {linkEx.Message}");
                    _ = BugReport.LogErrorAsync(linkEx, "Failed to open WebView2 download link.");
                    MessageBox.Show("Could not open the download link. Please visit https://developer.microsoft.com/en-us/microsoft-edge/webview2/ manually.", "Link Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }
    }

    private void WebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            IsSearching = false;
            StatusMessageText = "Web search loaded.";
            UpdateStatusBar();
        });
    }

    private void UpdateSearchUiVisibility()
    {
        // Show WebView for web search engines, hide for API search engines
        if (_settingsManager.SearchEngine is "BingWeb" or "GoogleWeb")
        {
            ImageScrollViewer.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
        }
        else
        {
            ImageScrollViewer.Visibility = Visibility.Visible;
            WebView.Visibility = Visibility.Collapsed;
        }
    }

    public string StatusMessageText
    {
        get => _statusMessageText;
        set
        {
            _statusMessageText = value;
            OnPropertyChanged();

            // Update the status bar directly since it's not bound
            if (Dispatcher.CheckAccess())
            {
                StatusMessage.Text = value;
            }
            else
            {
                Dispatcher.Invoke(() => StatusMessage.Text = value);
            }
        }
    }

    private void UpdateStatusBar()
    {
        // Ensure UI updates happen on the UI thread
        if (Dispatcher.CheckAccess())
        {
            // We're on the UI thread, safe to update directly
            SearchEngineDisplay = _settingsManager.SearchEngine;

            if (StatusMessage != null)
            {
                // Only show image count for API searches, not web views
                StatusImageCount.Text = _settingsManager.SearchEngine is "BingWeb" or "GoogleWeb" ? "N/A" : PanelImages.Count.ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            // We're on a background thread, dispatch to UI thread
            Dispatcher.Invoke(() =>
            {
                SearchEngineDisplay = _settingsManager.SearchEngine;

                if (StatusMessage != null)
                {
                    StatusImageCount.Text = _settingsManager.SearchEngine is "BingWeb" or "GoogleWeb" ? "N/A" : PanelImages.Count.ToString(CultureInfo.InvariantCulture);
                }
            });
        }
    }

    private void LstMissingImages_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && LstMissingImages.SelectedItem != null)
        {
            e.Handled = true;
            RemoveSelectedItem();
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void SetCheckedStateForThemeAndAccent()
    {
        AppLogger.Log("Setting checked state for theme and accent menu items.");
        LightTheme.IsChecked = _settingsManager.BaseTheme == "Light";
        DarkTheme.IsChecked = _settingsManager.BaseTheme == "Dark";

        foreach (var item in MenuAccentColors.Items.OfType<MenuItem>())
        {
            item.IsChecked = item.Name.Replace("Accent", "") == _settingsManager.AccentColor;
        }
    }

    private void ChangeBaseTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        var theme = menuItem.Name == "LightTheme" ? "Light" : "Dark";
        _settingsManager.BaseTheme = theme;
        ThemeManager.Current.ChangeTheme(Application.Current, $"{theme}.{_settingsManager.AccentColor}");
        _settingsManager.SaveSettings();

        LightTheme.IsChecked = theme == "Light";
        DarkTheme.IsChecked = theme == "Dark";
        AppLogger.Log($"Base theme changed to {theme}.");
        StatusMessageText = $"Theme changed to {theme}.";
    }

    private void ChangeAccentColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        var accent = menuItem.Name.Replace("Accent", "");
        _settingsManager.AccentColor = accent;
        ThemeManager.Current.ChangeTheme(Application.Current, $"{_settingsManager.BaseTheme}.{accent}");
        _settingsManager.SaveSettings();

        foreach (var item in MenuAccentColors.Items.OfType<MenuItem>())
        {
            item.IsChecked = false;
        }

        menuItem.IsChecked = true;
        AppLogger.Log($"Accent color changed to {accent}.");
        StatusMessageText = $"Accent color changed to {accent}.";
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Exit menu item clicked.");
        Close();
    }

    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Donate button clicked.");
        try
        {
            Process.Start(
                new ProcessStartInfo("https://www.purelogiccode.com/donate")
                    { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Unable to open the link\n\n" +
                            "The developer will try to fix it.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

            _ = BugReport.LogErrorAsync(ex, "Unable to open donation link.");
        }
    }

    private void BtnBrowseRomFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Browse ROM folder button clicked.");
        var dialog = new OpenFolderDialog { Title = "Please Choose Rom Folder" };
        if (dialog.ShowDialog() != true) return;

        TxtRomFolder.Text = dialog.FolderName;
        AppLogger.Log($"ROM folder selected: {dialog.FolderName}");
        StatusMessageText = $"ROM folder selected: {dialog.FolderName}";
    }

    private void BtnBrowseImageFolder_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Browse Image folder button clicked.");
        var dialog = new OpenFolderDialog { Title = "Please Choose Image Folder" };
        if (dialog.ShowDialog() != true) return;

        TxtImageFolder.Text = dialog.FolderName;
        _imageFolderPath = dialog.FolderName;
        AppLogger.Log($"Image folder selected: {dialog.FolderName}");
        StatusMessageText = $"Image folder selected: {dialog.FolderName}";

        // Initialize the watcher for the newly selected folder
        InitializeFileSystemWatcher();
    }

    private async void BtnCheckForMissingImages_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Log("Check for missing images button clicked.");
            var romFolderPath = TxtRomFolder.Text;
            var imageFolderPath = TxtImageFolder.Text;

            if (string.IsNullOrEmpty(romFolderPath) || string.IsNullOrEmpty(imageFolderPath))
            {
                MessageBox.Show("Please select both ROM and Image folders.", "Missing Information", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusMessageText = "Checking for missing images...";
                AppLogger.Log($"Checking ROMs in '{romFolderPath}' against images in '{imageFolderPath}'.");

                // Validate that directories exist and are accessible
                if (!Directory.Exists(romFolderPath))
                {
                    MessageBox.Show($"ROM folder does not exist: {romFolderPath}", "Invalid ROM Folder", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (!Directory.Exists(imageFolderPath))
                {
                    MessageBox.Show($"Image folder does not exist: {imageFolderPath}", "Invalid Image Folder", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var supportedExtensions = _settingsManager.SupportedExtensions.ToArray();

                // Check if supported extensions are available
                if (supportedExtensions.Length == 0)
                {
                    MessageBox.Show("No supported ROM file extensions configured. Please check your settings.",
                        "No Extensions Configured", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string[] romFiles;
                try
                {
                    romFiles = await Task.Run(() => supportedExtensions
                        .SelectMany(ext => Directory.GetFiles(romFolderPath, $"*.{ext}"))
                        .OrderBy(file => file)
                        .ToArray()).ConfigureAwait(false);
                    AppLogger.Log($"Found {romFiles.Length} ROM files with supported extensions.");
                }
                catch (DirectoryNotFoundException)
                {
                    MessageBox.Show($"ROM folder not found: {romFolderPath}", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (UnauthorizedAccessException)
                {
                    MessageBox.Show($"Access denied to ROM folder: {romFolderPath}", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Error accessing the ROM folder\n\n" +
                                    "The developer will try to fix it.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _ = BugReport.LogErrorAsync(ex, "Error accessing ROM folder.").ConfigureAwait(false);
                    return;
                }

                // Call the updated async version of CheckForMissingImages
                // This method now ONLY identifies missing PNGs. Conversion is handled by FileSystemWatcher.
                await CheckForMissingImages(LstMissingImages, imageFolderPath, romFiles).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                StatusMessageText = "An error occurred while checking for images.";
                MessageBox.Show("An error occurred while checking for images.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _ = BugReport.LogErrorAsync(ex, "Unexpected error in BtnCheckForMissingImages_Click.").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Unexpected error in BtnCheckForMissingImages_Click outer try-catch.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks for missing images for ROMs. It identifies ROMs that do not have a corresponding cover image
    /// in any of the recognized image formats (PNG, JPG, BMP, GIF, TIFF, WebP, AVIF).
    /// Image conversion for newly created files is now handled by the FileSystemWatcher in OnImageFileCreated.
    /// </summary>
    /// <param name="lstMissingImages">The ListBox to populate with names of ROMs still missing covers.</param>
    /// <param name="imageFolderPath">The path to the folder where images are stored.</param>
    /// <param name="romFiles">An array of full paths to ROM files.</param>
    private async Task CheckForMissingImages(ListBox lstMissingImages, string imageFolderPath, string[] romFiles)
    {
        AppLogger.Log("Starting check for missing images (all recognized formats)."); // Updated log message
        await Dispatcher.InvokeAsync(() => lstMissingImages.Items.Clear());
        StatusMessageText = "Scanning for missing covers...";

        // Define all recognized image extensions that can serve as a cover
        string[] recognizedCoverExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".avif"];

        foreach (var file in romFiles)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);
            var coverFound = false;

            // Check if any recognized image file exists for this ROM
            foreach (var ext in recognizedCoverExtensions)
            {
                var imagePath = Path.Combine(imageFolderPath, fileNameWithoutExtension + ext);
                if (File.Exists(imagePath))
                {
                    coverFound = true;
                    break; // Found a cover for this ROM, no need to check other extensions
                }
            }

            // If no cover was found after checking all recognized extensions, add to the missing list
            if (!coverFound)
            {
                await Dispatcher.InvokeAsync(() => lstMissingImages.Items.Add(fileNameWithoutExtension));
            }
        }

        await Dispatcher.InvokeAsync(() =>
        {
            AppLogger.Log($"Finished scanning. Found {lstMissingImages.Items.Count} missing images.");
            StatusMessageText = $"Found {lstMissingImages.Items.Count} missing covers.";
            UpdateMissingImagesCount();
        });
    }

    /// <summary>
    /// Converts an image from a source path to a PNG format at a destination path.
    /// Preserves the aspect ratio, dimensions, and transparency using SixLabors.ImageSharp.
    /// </summary>
    /// <param name="sourcePath">The path to the source image file.</param>
    /// <param name="destinationPath">The path where the PNG image will be saved.</param>
    /// <returns>True if conversion was successful, false otherwise.</returns>
    private static async Task<bool> ConvertImageToPng(string sourcePath, string destinationPath)
    {
        try
        {
            AppLogger.Log($"Attempting to convert '{sourcePath}' to PNG at '{destinationPath}'.");

            // Ensure the destination directory exists
            var destDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // ImageSharp automatically handles various formats and preserves properties
            // when loading and saving, unless explicit resizing/manipulation is applied.
            using (var image = await Image.LoadAsync(sourcePath).ConfigureAwait(false))
            {
                // Save as PNG. ImageSharp handles aspect ratio, dimensions, and alpha channel by default.
                await image.SaveAsPngAsync(destinationPath).ConfigureAwait(false);
            }

            AppLogger.Log($"Successfully converted '{sourcePath}' to '{destinationPath}'.");
            return true;
        }
        catch (FileNotFoundException ex)
        {
            AppLogger.Log($"Source image file not found during conversion: '{sourcePath}'. Error: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, $"Source image file not found for conversion: {sourcePath}");
            return false;
        }
        catch (UnknownImageFormatException ex)
        {
            AppLogger.Log($"Unknown image format for conversion: '{sourcePath}'. Error: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, $"Unknown image format for conversion: {sourcePath}");
            return false;
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error converting image '{sourcePath}' to PNG: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, $"Error converting image to PNG: {sourcePath}");
            return false;
        }
    }

    private void UpdateMissingImagesCount()
    {
        Dispatcher.Invoke(() =>
        {
            LabelMissingRoms.Content = $"MISSING COVERS: {LstMissingImages.Items.Count}";
            UpdateStatusBar();
        });
    }

    private void LstMissingImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        AppLogger.Log("Missing images selection changed.");

        // Stop the timer if it's running
        _selectionDelayTimer?.Stop();

        // Cancel any ongoing search immediately
        if (_searchCts is not null)
        {
            AppLogger.Log("Cancelling previous search.");
            _searchCts.Cancel();
            _searchCts.Dispose();
            _searchCts = null;
        }

        if (LstMissingImages.SelectedItem is not string selectedFile)
        {
            PanelImages.Clear();
            LblSearchQuery.Content = "";
            IsSearching = false; // Ensure IsSearching is false when no item is selected
            StatusMessageText = "Ready"; // Reset status
            if (WebView?.CoreWebView2 != null)
            {
                WebView.CoreWebView2.Navigate("about:blank"); // Clear WebView content
            }

            return;
        }

        // --- Automatic copy to clipboard ---
        try
        {
            Clipboard.SetText(selectedFile);
            AppLogger.Log($"Automatically copied filename to clipboard: '{selectedFile}'");
            StatusMessageText = $"Copied '{selectedFile}' to clipboard.";
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to automatically copy filename to clipboard: {ex.Message}");
        }

        // Clear UI immediately to prevent showing stale data
        PanelImages.Clear();
        LblSearchQuery.Content = "Searching...";
        IsSearching = true;
        StatusMessageText = "Searching for images...";

        UpdateSearchUiVisibility(); // Update visibility based on the selected engine

        // Start the debounce timer
        _selectionDelayTimer?.Start();
        AppLogger.Log("Selection change debounce timer started.");
    }

    /// <summary>
    /// Cleans a ROM filename to create a better web search query.
    /// Removes common emulator tags like (USA), [!], (Rev A), etc.
    /// </summary>
    /// <param name="fileName">The original filename.</param>
    /// <returns>A cleaner string for searching.</returns>
    private static string CleanSearchQuery(string fileName)
    {
        // This regex matches common patterns in parentheses or square brackets.
        // e.g., (USA), (Europe), (Japan), (Brazil), (En,Ja), [!], (Rev A), (v1.1), (Unl), (Mega Drive 4)
        var cleanedName = Regex.Replace(fileName, @"\s*(\(.*?\)|\\[.*?\\])", "").Trim();

        // If cleaning removed everything (unlikely), fall back to the original name
        return string.IsNullOrWhiteSpace(cleanedName) ? fileName : cleanedName;
    }

    private async void SelectionDelayTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Stop the timer
            _selectionDelayTimer?.Stop();
            AppLogger.Log("Selection timer ticked.");

            // Create new cancellation token source for current request
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;

            if (LstMissingImages.SelectedItem is not string selectedFile)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    PanelImages.Clear();
                    UpdateStatusBar();
                    LblSearchQuery.Content = "";
                    IsSearching = false;
                    StatusMessageText = "Ready";
                });
                return;
            }

            _selectedGameFileName = selectedFile;

            // Determine search query
            string searchTerm;
            if (_settingsManager.UseMameDescriptions && _mameLookup.TryGetValue(selectedFile, out var mameDescription) && !string.IsNullOrWhiteSpace(mameDescription))
            {
                searchTerm = mameDescription;
                AppLogger.Log($"Using MAME description for search: '{selectedFile}' -> '{mameDescription}'");
            }
            else
            {
                searchTerm = CleanSearchQuery(selectedFile);
                AppLogger.Log($"Using cleaned filename for search: '{selectedFile}' -> '{searchTerm}'");
            }

            var extraQuery = TxtExtraQuery.Text?.Trim();
            var searchQuery = !string.IsNullOrWhiteSpace(extraQuery) ? $"\"{searchTerm}\" {extraQuery}" : $"\"{searchTerm}\"";

            switch (_settingsManager.SearchEngine)
            {
                case "BingWeb":
                    await HandleBingWebSearch(searchQuery);
                    return;
                case "GoogleWeb":
                    await HandleGoogleWebSearch(searchQuery);
                    return;
                default:
                    await HandleApiSearch(searchQuery, token);
                    break;
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Unexpected error in SelectionDelayTimer_Tick.").ConfigureAwait(false);
        }
    }

    private async Task HandleApiSearch(string searchQuery, CancellationToken token)
    {
        try
        {
            // For API searches, we typically want exact matches, so quoting the search term is good.
            // Ensure any existing quotes are handled to avoid double quoting.
            var apiSearchQuery = $"\"{searchQuery.Replace("\"", "")}\"";
            AppLogger.Log($"Starting API image search for '{_selectedGameFileName}' using {_settingsManager.SearchEngine}.");
            AppLogger.Log($"API Search query: {apiSearchQuery}");

            List<ImageData> coverImageUrls;
            try
            {
                coverImageUrls = await FetchImagesWithRetry(apiSearchQuery, _settingsManager.SearchEngine, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("API Key is not set"))
            {
                await Dispatcher.InvokeAsync(static () =>
                {
                    MessageBox.Show("Please configure your API keys in Settings > API Settings.", "Missing API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                coverImageUrls = new List<ImageData>();
            }
            catch (InvalidOperationException)
            {
                await Dispatcher.InvokeAsync(static () =>
                {
                    MessageBox.Show("Error fetching the images.", "API Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                coverImageUrls = new List<ImageData>();
            }
            catch (OperationCanceledException)
            {
                AppLogger.Log("Image fetch operation was canceled.");
                await Dispatcher.InvokeAsync(() => { StatusMessageText = "Search canceled."; });
                return;
            }

            if (token.IsCancellationRequested) return;

            await Dispatcher.InvokeAsync(() =>
            {
                PanelImages.Clear();
                UpdateStatusBar();
                ImageScrollViewer.ScrollToTop();
            });

            var thumbnailSize = _settingsManager.ThumbnailSize;
            if (coverImageUrls.Count > 0)
            {
                AppLogger.Log($"Fetched {coverImageUrls.Count} images from {_settingsManager.SearchEngine}.");
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusMessageText = $"Found {coverImageUrls.Count} images.";
                    LblSearchQuery.Content = new TextBlock
                    {
                        Inlines =
                        {
                            new Run(apiSearchQuery) { FontWeight = FontWeights.Bold },
                            new Run($" (Fetched {coverImageUrls.Count} images from {_settingsManager.SearchEngine})")
                        }
                    };
                    foreach (var result in coverImageUrls)
                    {
                        result.ThumbnailWidth = thumbnailSize;
                        result.ThumbnailHeight = thumbnailSize;
                        PanelImages.Add(result);
                    }

                    UpdateStatusBar();
                });
            }
            else
            {
                AppLogger.Log($"No results found for query: {apiSearchQuery}");
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusMessageText = "No images found.";
                    LblSearchQuery.Content = $"{apiSearchQuery} (No results found from {_settingsManager.SearchEngine})";
                    PanelImages.Add(new ImageData
                    {
                        ImageName = "No Cover Image Found",
                        ImagePath = "pack://application:,,,/images/default.png",
                        ThumbnailWidth = thumbnailSize,
                        ThumbnailHeight = thumbnailSize
                    });
                });
            }
        }
        catch (OperationCanceledException)
        {
            AppLogger.Log("Image fetch operation was canceled.");
            await Dispatcher.InvokeAsync(() => { StatusMessageText = "Search canceled."; });
        }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    StatusMessageText = "Error fetching images.";
                    MessageBox.Show("There was an error fetching the images", "Warning", MessageBoxButton.OK, MessageBoxImage.Error);
                });
                _ = BugReport.LogErrorAsync(ex, "Error fetching images in HandleApiSearch.");
            }
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    IsSearching = false;
                    UpdateStatusBar();
                });
            }
        }
    }

    private async Task<List<ImageData>> FetchImagesWithRetry(string searchQuery, string searchEngine, CancellationToken cancellationToken = default)
    {
        const int maxRetries = 1;
        var retryCount = 0;

        while (retryCount <= maxRetries)
        {
            try
            {
                switch (searchEngine)
                {
                    case "Google":
                        var googleProvider = new Google();
                        Google.LoadApiKeyFromSettings(_settingsManager);
                        return await googleProvider.FetchImagesFromGoogleAsync(searchQuery, _settingsManager, cancellationToken).ConfigureAwait(false);
                    default:
                        return new List<ImageData>();
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("API Key is not set") && retryCount < maxRetries)
            {
                retryCount++;

                // Show API settings dialog
                var result = MessageBox.Show($"API Key is not set.\n\n" +
                                             $"Would you like to configure your API keys now?", "Missing API Key", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var apiSettingsWindow = new ApiSettingsWindow(_settingsManager)
                    {
                        Owner = this
                    };

                    if (apiSettingsWindow.ShowDialog() != true)
                    {
                        return new List<ImageData>(); // User cancelled
                    }
                    // Continue to retry
                }
                else
                {
                    return new List<ImageData>(); // User doesn't want to configure
                }
            }
            catch (InvalidOperationException)
            {
                MessageBox.Show("Error fetching the images", "API Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return new List<ImageData>();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Handle cancellation gracefully - rethrow to let caller handle
                throw;
            }
        }

        return new List<ImageData>();
    }

    private void BtnRemoveSelectedItem_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Remove selected item button clicked.");
        RemoveSelectedItem();
    }

    private void RemoveSelectedItem()
    {
        if (LstMissingImages.SelectedItem is string itemToRemove)
        {
            AppLogger.Log($"Removing '{itemToRemove}' from list via UI action.");
            RemoveItemFromListByName(itemToRemove);
            StatusMessageText = $"Removed '{itemToRemove}' from the list.";
        }
        else
        {
            MessageBox.Show("Please select a listed item to delete.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Context menu click handler for "Remove Item from the list"
    private void RemoveItemFromList_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Context menu 'Remove Item from the list' clicked.");
        RemoveSelectedItem();
    }

    // Context menu click handler for "Copy FileName"
    private void CopyFileName_Click(object sender, RoutedEventArgs e)
    {
        if (LstMissingImages.SelectedItem is string selectedFileName)
        {
            AppLogger.Log($"Context menu 'Copy FileName' clicked. Copying: '{selectedFileName}'");
            try
            {
                Clipboard.SetText(selectedFileName);
                StatusMessageText = $"Copied '{selectedFileName}' to clipboard.";
            }
            catch (Exception ex)
            {
                AppLogger.Log($"Failed to copy filename to clipboard: {ex.Message}");
                MessageBox.Show("Failed to copy filename to clipboard.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _ = BugReport.LogErrorAsync(ex, "Failed to copy filename from context menu.");
            }
        }
        else
        {
            AppLogger.Log("Context menu 'Copy FileName' clicked, but no item was selected.");
            MessageBox.Show("No item selected to copy.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Context menu opening handler to enable/disable items
    private void LstMissingImages_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var hasSelection = LstMissingImages.SelectedItem != null;
        if (ContextMenuRemoveItem != null)
        {
            ContextMenuRemoveItem.IsEnabled = hasSelection;
        }

        if (ContextMenuCopyFileName != null)
        {
            ContextMenuCopyFileName.IsEnabled = hasSelection;
        }
    }

    private void SetThumbnailSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            !int.TryParse(menuItem.Header.ToString()?.Replace(" pixels", ""), out var size)) return;

        _settingsManager.ThumbnailSize = size;
        _settingsManager.SaveSettings();

        ApplyThumbnailSizeInPanel(size);
        UpdateCheckedState();
        AppLogger.Log($"Thumbnail size set to {size}.");
        StatusMessageText = $"Thumbnail size set to {size}px.";
    }

    private void ApplyThumbnailSizeInPanel(int size)
    {
        // This is the corrected logic.
        // It iterates through the existing collection and updates the properties.
        // The UI updates automatically because ImageData now implements INotifyPropertyChanged.
        foreach (var imageData in PanelImages)
        {
            imageData.ThumbnailWidth = size;
            imageData.ThumbnailHeight = size;
        }
    }

    private void UpdateCheckedState()
    {
        var savedSize = _settingsManager.ThumbnailSize;
        foreach (var item in ThumbnailSizeMenu.Items.OfType<MenuItem>())
        {
            if (int.TryParse(item.Header.ToString()?.Replace(" pixels", ""), out var tagSize))
            {
                item.IsChecked = savedSize == tagSize;
            }
        }
    }

    private void UpdateMenuItems()
    {
        foreach (var item in SearchEngineMenu.Items.OfType<MenuItem>())
        {
            item.IsChecked = item.Tag?.ToString() == _settingsManager.SearchEngine;
        }
    }

    private void SetSearchEngine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: not null } menuItem) return;

        try
        {
            _settingsManager.SearchEngine = menuItem.Tag.ToString() ?? throw new InvalidOperationException();
            _settingsManager.SaveSettings();
            UpdateMenuItems();
            AppLogger.Log($"Search engine set to '{_settingsManager.SearchEngine}'.");
            StatusMessageText = $"Search engine set to {_settingsManager.SearchEngine}.";
            SearchEngineDisplay = _settingsManager.SearchEngine;

            UpdateSearchUiVisibility(); // Update visibility immediately
            PanelImages.Clear(); // Clear API results when switching to the web view
            if (WebView?.CoreWebView2 != null)
            {
                WebView.CoreWebView2.Navigate("about:blank"); // Clear WebView content
            }

            LblSearchQuery.Content = ""; // Clear search query label
        }
        catch (Exception ex)
        {
            MessageBox.Show("There was an error saving the search engine settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReport.LogErrorAsync(ex, "Error saving search engine setting.");
        }
    }

    private void ToggleDebugWindow_Click(object sender, RoutedEventArgs e)
    {
        ToggleDebugWindowVisibility();
        AppLogger.Log("Toggled log window visibility.");

        // Update the menu item checked state
        if (App.LogWindow != null)
        {
            ToggleDebugWindow.IsChecked = App.LogWindow.IsVisible;
        }
    }

    private static void ToggleDebugWindowVisibility()
    {
        var debugWindow = App.LogWindow ?? new DebugWindow();

        if (debugWindow.IsVisible)
        {
            debugWindow.Hide();
        }
        else
        {
            // Subscribe to the WindowHidden event to update the menu state
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                debugWindow.WindowHidden -= mainWindow.OnDebugWindowHidden;
                debugWindow.WindowHidden += mainWindow.OnDebugWindowHidden;
            }

            debugWindow.Show();
            debugWindow.Activate();
        }
    }

    private void OnDebugWindowHidden(object? sender, EventArgs e)
    {
        // Update the menu item checked state when the debug window is hidden
        if (ToggleDebugWindow != null)
        {
            ToggleDebugWindow.IsChecked = false;
        }
    }

    private void ApiSettings_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("API Settings menu item clicked.");
        try
        {
            var apiSettingsWindow = new ApiSettingsWindow(_settingsManager)
            {
                Owner = this
            };

            if (apiSettingsWindow.ShowDialog() == true)
            {
                StatusMessageText = "API settings updated successfully.";
                AppLogger.Log("API settings updated through UI.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("Error opening API settings.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReport.LogErrorAsync(ex, "Error opening API settings window.");
        }
    }

    private void ToggleMameDescriptions_Click(object sender, RoutedEventArgs e)
    {
        _settingsManager.UseMameDescriptions = ToggleMameDescriptions.IsChecked;
        _settingsManager.SaveSettings();
        StatusMessageText = $"MAME descriptions turned {(ToggleMameDescriptions.IsChecked ? "on" : "off")}.";
        AppLogger.Log($"MAME descriptions turned {(ToggleMameDescriptions.IsChecked ? "on" : "off")}.");
    }

    private void ShowAboutWindow_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow().ShowDialog();
    }

    private static void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        App.LogWindow?.ForceClose();
        App.LogWindow?.Close();
    }

    public void Dispose()
    {
        AppLogger.Log("Disposing MainWindow resources.");
        _searchCts?.Dispose();
        _selectionDelayTimer?.Stop();
        _selectionDelayTimer = null;
        _imageFolderWatcher?.Dispose();
        WebView?.Dispose();
        GC.SuppressFinalize(this);
    }
}