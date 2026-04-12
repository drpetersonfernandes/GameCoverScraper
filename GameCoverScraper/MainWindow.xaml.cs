using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
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
using ImageMagick;
using System.Diagnostics;

namespace GameCoverScraper;

public partial class MainWindow : INotifyPropertyChanged, IDisposable
{
    private List<MameManager> _machines;
    private Dictionary<string, string> _mameLookup;
    private FileSystemWatcher? _imageFolderWatcher;
    private string? _imageFolderPath;
    private string? _selectedGameFileName;
    private CancellationTokenSource? _searchCts;
    private DispatcherTimer? _selectionDelayTimer;
    private DispatcherTimer? _statusMessageTimer;
    private string? _pendingStatusMessage;
    private bool _isStatusMessageTimed;
    private bool _isWebViewInitializing;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ImageData> PanelImages { get; set; }

    public bool IsSearching
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    }

    private SettingsManager? _settingsManager;

    /// <summary>
    /// Gets the initialized settings manager. Throws if accessed before initialization.
    /// </summary>
    private SettingsManager Settings => _settingsManager ?? throw new InvalidOperationException("SettingsManager not initialized. Ensure MainWindow_Loaded has completed.");

    public string SearchEngineDisplay
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
        }
    } = string.Empty;

    private readonly string? _startupImageFolder;
    private readonly string? _startupRomFolder;

    public MainWindow(string? startupImageFolder = null, string? startupRomFolder = null)
    {
        InitializeComponent();
        AppLogger.Log("MainWindow initializing...");
        DataContext = this;
        PanelImages = [];
        TxtExtraQuery.Text = "";

        // Store startup arguments for later initialization
        _startupImageFolder = startupImageFolder;
        _startupRomFolder = startupRomFolder;

        // Initialize collections (required for DataContext binding)
        _machines = [];
        _mameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Wire up event handlers
        Closing += MainWindow_Closing;
        Closed += (_, _) => Dispose();
        Loaded += MainWindow_Loaded;

        AppLogger.Log("MainWindow constructor completed (lightweight).");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AppLogger.Log("MainWindow_Loaded started...");

            // Initialize timers
            InitializeTimers();

            // Initialize settings
            _settingsManager = new SettingsManager();
            Settings.LoadSettings();

            // Update UI based on settings
            SetCheckedStateForThemeAndAccent();
            UpdateCheckedState();
            UpdateMenuItems();

            // Subscribe to the debug window hidden event if it exists
            if (App.LogWindow != null)
            {
                App.LogWindow.WindowHidden -= OnDebugWindowHidden;
                App.LogWindow.WindowHidden += OnDebugWindowHidden;
            }

            // Wire up collection changed handler for status bar
            PanelImages.CollectionChanged += (_, _) =>
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

            // Add keyboard and context menu handlers
            LstMissingImages.PreviewKeyDown += LstMissingImages_PreviewKeyDown;
            LstMissingImages.ContextMenuOpening += LstMissingImages_ContextMenuOpening;

            // Load MAME data asynchronously
            await LoadMameDataAsync();

            ToggleMameDescriptions.IsChecked = Settings.UseMameDescriptions;

            // Initialize WebView2
            await InitializeWebViewAsync();

            // Handle startup arguments
            HandleStartupArguments();

            AppLogger.Log("MainWindow_Loaded completed.");
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Error in MainWindow_Loaded method.");
        }
    }

    private void InitializeTimers()
    {
        _selectionDelayTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _selectionDelayTimer.Tick += SelectionDelayTimer_Tick;
        AppLogger.Log("Selection delay timer initialized.");

        _statusMessageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5)
        };
        _statusMessageTimer.Tick += StatusMessageTimer_Tick;
        AppLogger.Log("Status message timer initialized.");
    }

    private async Task LoadMameDataAsync()
    {
        try
        {
            _machines = await Task.Run(static () => MameManager.LoadFromDat());
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
            _ = BugReport.LogErrorAsync(ex, "MAME data file 'mame.dat' is corrupted or in an invalid format.");
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
    }

    private void HandleStartupArguments()
    {
        if (!string.IsNullOrEmpty(_startupImageFolder))
        {
            TxtImageFolder.Text = _startupImageFolder;
            _imageFolderPath = _startupImageFolder;
            AppLogger.Log($"Image folder set from startup argument: '{_startupImageFolder}'");
        }

        if (!string.IsNullOrEmpty(_startupRomFolder))
        {
            TxtRomFolder.Text = _startupRomFolder;
            AppLogger.Log($"ROM folder set from startup argument: '{_startupRomFolder}'");
        }

        // If both folders are provided, automatically trigger the check
        if (!string.IsNullOrEmpty(TxtImageFolder.Text) && !string.IsNullOrEmpty(TxtRomFolder.Text))
        {
            AppLogger.Log("Both startup folders provided. Initializing FileSystemWatcher and checking for missing images automatically.");
            InitializeFileSystemWatcher();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        BtnCheckForMissingImages_Click(this, new RoutedEventArgs());
                    });
                }
                catch (Exception ex)
                {
                    _ = BugReport.LogErrorAsync(ex, "Error handling startup arguments");
                }
            });
        }
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
            // Use Invoke (synchronous) to ensure thread-safe access to the UI collection
            var isFileInMissingList = Dispatcher.Invoke(() =>
            {
                return LstMissingImages.Items.Cast<object>()
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
                if (!await WaitForFileAccess(e.FullPath, 10000).ConfigureAwait(false))
                {
                    // File remained locked, log it and inform the user.
                    var timeoutMessage = $"File '{e.Name}' remained locked and could not be processed. Please try saving it again.";
                    AppLogger.Log(timeoutMessage);
                    // We still want to report this, as it's an unexpected condition.
                    _ = BugReport.LogErrorAsync(new TimeoutException($"File '{e.FullPath}' remained locked after 10000ms."), $"Error in OnImageFileCreated for file {e.FullPath}");

                    await Dispatcher.InvokeAsync(() =>
                    {
                        MessageBox.Show(timeoutMessage, "File Locked", MessageBoxButton.OK, MessageBoxImage.Warning);
                        StatusMessageText = $"Could not process '{e.Name}' (file was locked).";
                    });
                    return; // Abort processing this file.
                }

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
                                _ = BugReport.LogErrorAsync(deleteEx, $"Failed to delete original file after conversion: {e.FullPath}");
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

    private static async Task<bool> WaitForFileAccess(string filePath, int timeoutMilliseconds)
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
                return true; // Success
            }
            catch (IOException)
            {
                // File is locked, wait a bit before retrying.
                await Task.Delay(currentDelay).ConfigureAwait(false);
                // Exponentially increase the delay, up to maxDelay
                currentDelay = Math.Min(currentDelay * 2, maxDelay);
            }
        }

        return false;
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
                // Safely navigate WebView to blank - check CoreWebView2 is initialized
                WebView?.CoreWebView2?.Navigate("about:blank");
            }
        }

        PlaySound.PlayClickSound();
        UpdateMissingImagesCount();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            if (_isWebViewInitializing || WebView.CoreWebView2 != null) return;

            _isWebViewInitializing = true;

            try
            {
                AppLogger.Log("Initializing WebView2...");

                // Use LocalAppData for the user data folder to avoid permission issues
                // and potential "Operation aborted" errors if the default folder is locked or inaccessible.
                var userDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameCoverScraper", "WebView2Data");

                try
                {
                    if (!Directory.Exists(userDataFolder))
                    {
                        Directory.CreateDirectory(userDataFolder);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Log($"Warning: Could not create WebView2 user data folder: {ex.Message}. Falling back to default.");
                    _ = BugReport.LogErrorAsync(ex, "Failed to create WebView2 user data folder.");
                    userDataFolder = null; // Fallback to default if we can't create the folder
                }

                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await WebView.EnsureCoreWebView2Async(env);
                WebView.NavigationCompleted += WebView_NavigationCompleted;

                // Ensure right-click menus are enabled (this is the default, but good to be explicit)
                if (WebView.CoreWebView2 != null)
                {
                    WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                    AppLogger.Log("WebView2 default context menus enabled.");
                }

                AppLogger.Log("WebView2 initialized successfully.");
            }
            catch (WebView2RuntimeNotFoundException ex)
            {
                AppLogger.Log($"WebView2 Runtime not found: {ex.Message}");
                _ = BugReport.LogErrorAsync(ex, "WebView2 initialization failed: Runtime not found.");

                // The Evergreen Bootstrapper is a small installer that downloads and installs the latest compatible WebView2 Runtime.
                const string webView2DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

                var result = MessageBox.Show("The web browser component (Microsoft Edge WebView2 Runtime) is required for web search functionality, but it's not installed on your system.\n\n" +
                                             "Would you like to download it from Microsoft's official website now?", "WebView2 Runtime Missing", MessageBoxButton.YesNo, MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(webView2DownloadUrl) { UseShellExecute = true });
                        AppLogger.Log($"Opened WebView2 Runtime download URL for user: {webView2DownloadUrl}");
                    }
                    catch (Exception linkEx)
                    {
                        AppLogger.Log($"Failed to open WebView2 download link: {linkEx.Message}");
                        _ = BugReport.LogErrorAsync(linkEx, "Failed to open WebView2 download link.");
                        MessageBox.Show($"Could not open the download link automatically. Please visit the following URL in your browser:\n\n{webView2DownloadUrl}",
                            "Link Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80004004)) // E_ABORT
            {
                AppLogger.Log("WebView2 initialization was aborted (E_ABORT). This might happen if the control is not yet in the visual tree or if multiple initializations are attempted.");
                _ = BugReport.LogErrorAsync(ex, "WebView2 initialization was aborted (E_ABORT).");

                MessageBox.Show(
                    "The web browser component initialization was aborted.\n\n" +
                    "This can occur if the component is already being initialized or if there are permission issues with the data folder.\n\n" +
                    "Please try restarting the application.",
                    "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                AppLogger.Log($"WebView2 initialization failed with an unexpected error: {ex.Message}");
                _ = BugReport.LogErrorAsync(ex, "WebView2 initialization failed with an unexpected error.");

                MessageBox.Show(
                    "An unexpected error occurred while initializing the web browser component (WebView2).\n\n" +
                    "Please try restarting the application. If the problem persists, ensure Microsoft Edge is up to date.",
                    "WebView2 Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _isWebViewInitializing = false;
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "WebView2 initialization failed with an unexpected error.");
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
        // Use case-insensitive comparison to handle any case variations in settings
        var searchEngine = Settings.SearchEngine.ToLowerInvariant();
        if (searchEngine is "bingweb" or "googleweb")
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
        get;
        set
        {
            field = value;
            if (_isStatusMessageTimed)
            {
                // Queue the new message if a timed message is active
                _pendingStatusMessage = value;
            }
            else
            {
                // Update the UI on the dispatcher thread
                if (Dispatcher.CheckAccess())
                {
                    StatusMessage.Text = value;
                }
                else
                {
                    Dispatcher.Invoke(() => StatusMessage.Text = value);
                }
            }

            OnPropertyChanged();
        }
    } = "Ready";

    private void UpdateStatusBar()
    {
        // Ensure UI updates happen on the UI thread
        // Use null-conditional operators to prevent NullReferenceException
        if (Dispatcher.CheckAccess())
        {
            // We're on the UI thread, safe to update directly
            SearchEngineDisplay = Settings.SearchEngine;

            if (StatusMessage != null)
            {
                // Only show image count for API searches, not web views
                // Use case-insensitive comparison
                var searchEngine = Settings.SearchEngine.ToLowerInvariant();
                StatusImageCount.Text = searchEngine is "bingweb" or "googleweb" ? "N/A" : PanelImages.Count.ToString(CultureInfo.InvariantCulture);
            }
        }
        else
        {
            // We're on a background thread, dispatch to UI thread
            Dispatcher.Invoke(() =>
            {
                SearchEngineDisplay = Settings.SearchEngine;

                if (StatusMessage != null)
                {
                    var searchEngine = Settings.SearchEngine.ToLowerInvariant();
                    StatusImageCount.Text = searchEngine is "bingweb" or "googleweb" ? "N/A" : PanelImages.Count.ToString(CultureInfo.InvariantCulture);
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
        LightTheme.IsChecked = Settings.BaseTheme == "Light";
        DarkTheme.IsChecked = Settings.BaseTheme == "Dark";

        foreach (var item in MenuAccentColors.Items.OfType<MenuItem>())
        {
            item.IsChecked = item.Name.Replace("Accent", "") == Settings.AccentColor;
        }
    }

    private void ChangeBaseTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        var theme = menuItem.Name == "LightTheme" ? "Light" : "Dark";
        Settings.BaseTheme = theme;
        ThemeManager.Current.ChangeTheme(Application.Current, $"{theme}.{Settings.AccentColor}");
        Settings.SaveSettings();

        LightTheme.IsChecked = theme == "Light";
        DarkTheme.IsChecked = theme == "Dark";
        AppLogger.Log($"Base theme changed to {theme}.");
        StatusMessageText = $"Theme changed to {theme}.";
    }

    private void ChangeAccentColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        var accent = menuItem.Name.Replace("Accent", "");
        Settings.AccentColor = accent;
        ThemeManager.Current.ChangeTheme(Application.Current, $"{Settings.BaseTheme}.{accent}");
        Settings.SaveSettings();

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

                var supportedExtensions = Settings.SupportedExtensions.ToArray();

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
                        .SelectMany(ext => Directory.GetFiles(romFolderPath, $"*.{ext}", SearchOption.AllDirectories))
                        .OrderBy(static file => file)
                        .ToArray()).ConfigureAwait(false);
                    AppLogger.Log($"Found {romFiles.Length} ROM files with supported extensions.");
                }
                catch (DirectoryNotFoundException ex)
                {
                    _ = BugReport.LogErrorAsync(ex, $"ROM folder not found: {romFolderPath}");
                    MessageBox.Show($"ROM folder not found: {romFolderPath}", "Directory Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                catch (UnauthorizedAccessException ex)
                {
                    _ = BugReport.LogErrorAsync(ex, $"Access denied to ROM folder: {romFolderPath}");
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
        AppLogger.Log("Starting check for missing images (all recognized formats).");
        StatusMessageText = "Scanning for missing covers...";

        // Build a HashSet of all existing image filenames (without extension, case-insensitive) for O(1) lookups
        var existingImageNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Enumerate all image files once and extract their base names
        string[] imageExtensions = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.tiff", "*.tif", "*.webp", "*.avif"];
        foreach (var pattern in imageExtensions)
        {
            foreach (var imagePath in Directory.EnumerateFiles(imageFolderPath, pattern))
            {
                existingImageNames.Add(Path.GetFileNameWithoutExtension(imagePath));
            }
        }

        // Find ROMs that don't have a corresponding image
        var missingItems = romFiles
            .Select(Path.GetFileNameWithoutExtension)
            .Where(romName => romName != null && !existingImageNames.Contains(romName))
            .OrderBy(static x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Batch all UI updates into a single Dispatcher.InvokeAsync call
        await Dispatcher.InvokeAsync(() =>
        {
            lstMissingImages.Items.Clear();
            foreach (var item in missingItems)
            {
                lstMissingImages.Items.Add(item);
            }

            AppLogger.Log($"Finished scanning. Found {lstMissingImages.Items.Count} missing images.");
            StatusMessageText = $"Found {lstMissingImages.Items.Count} missing covers.";
            UpdateMissingImagesCount();
        });
    }

    /// <summary>
    /// Converts an image from a source path to a PNG format at a destination path.
    /// Preserves the aspect ratio, dimensions, and transparency using Magick.NET (ImageMagick).
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

            // Magick.NET automatically handles various formats and preserves properties
            // when loading and saving, unless explicit resizing/manipulation is applied.
            using (var image = new MagickImage(sourcePath))
            {
                // Save as PNG. Magick.NET handles aspect ratio, dimensions, and alpha channel by default.
                await image.WriteAsync(destinationPath, MagickFormat.Png).ConfigureAwait(false);
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
        catch (MagickMissingDelegateErrorException ex)
        {
            AppLogger.Log($"Unknown image format for conversion: '{sourcePath}'. Error: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, $"Unknown image format (no codec) for conversion: {sourcePath}");
            return false;
        }
        catch (MagickCorruptImageErrorException ex)
        {
            AppLogger.Log($"Corrupt image file for conversion: '{sourcePath}'. Error: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, $"Corrupt image file for conversion: {sourcePath}");
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
        // Use Interlocked.Exchange for thread-safe disposal to prevent race conditions
        var oldCts = Interlocked.Exchange(ref _searchCts, null);
        if (oldCts is not null)
        {
            AppLogger.Log("Cancelling previous search.");
            oldCts.Cancel();
            oldCts.Dispose();
        }

        if (LstMissingImages.SelectedItem is not string selectedFile)
        {
            PanelImages.Clear();
            LblSearchQuery.Content = "";
            IsSearching = false; // Ensure IsSearching is false when no item is selected
            StatusMessageText = "Ready"; // Reset status
            WebView?.CoreWebView2?.Navigate("about:blank"); // Clear WebView content

            return;
        }

        // --- Automatic copy to clipboard ---
        try
        {
            if (string.IsNullOrEmpty(selectedFile)) return;

            Clipboard.SetText(selectedFile);
            AppLogger.Log($"Automatically copied filename to clipboard: '{selectedFile}'");

            // Lock the status message for 5 seconds
            _isStatusMessageTimed = true;
            StatusMessage.Text = $"Automatically copied filename to clipboard: '{selectedFile}'";
            _statusMessageTimer?.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to automatically copy filename to clipboard: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Failed to automatically copy filename to clipboard.");
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
        var cleanedName = MyRegex().Replace(fileName, "").Trim();

        // If cleaning removed everything (unlikely), fall back to the original name
        return string.IsNullOrWhiteSpace(cleanedName) ? fileName : cleanedName;
    }

    /// <summary>
    /// Sanitizes a filename to prevent path traversal attacks.
    /// Removes path separator characters and other potentially dangerous characters.
    /// </summary>
    /// <param name="fileName">The original filename.</param>
    /// <returns>A sanitized filename safe for use in path construction.</returns>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "unnamed";
        }

        // Remove path traversal sequences and invalid path characters
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName
            .Replace("..", "")
            .Replace("/", "")
            .Replace("\\", "")
            .Select(c => invalidChars.Contains(c) ? '_' : c)
            .ToArray());

        // Trim whitespace and dots from ends
        sanitized = sanitized.Trim().TrimEnd('.');

        // Ensure we have something left
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    private void SelectionDelayTimer_Tick(object? sender, EventArgs e)
    {
        // Fire-and-forget async operation to avoid deadlock risk
        // The timer tick is a UI thread event, so we can safely access UI elements directly
        _ = SelectionDelayTimer_TickAsync();
    }

    private async Task SelectionDelayTimer_TickAsync()
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
                // Direct UI access - we're on UI thread
                PanelImages.Clear();
                UpdateStatusBar();
                LblSearchQuery.Content = "";
                IsSearching = false;
                StatusMessageText = "Ready";
                return;
            }

            _selectedGameFileName = selectedFile;

            // Determine search query
            string searchTerm;
            if (Settings.UseMameDescriptions && _mameLookup.TryGetValue(selectedFile, out var mameDescription) && !string.IsNullOrWhiteSpace(mameDescription))
            {
                searchTerm = mameDescription;
                AppLogger.Log($"Using MAME description for search: '{selectedFile}' -> '{mameDescription}'");
            }
            else
            {
                searchTerm = CleanSearchQuery(selectedFile);
                AppLogger.Log($"Using cleaned filename for search: '{selectedFile}' -> '{searchTerm}'");
            }

            var extraQuery = TxtExtraQuery.Text.Trim();
            var searchQuery = !string.IsNullOrWhiteSpace(extraQuery) ? $"\"{searchTerm}\" {extraQuery}" : $"\"{searchTerm}\"";

            switch (Settings.SearchEngine)
            {
                case "BingWeb":
                    await HandleBingWebSearch(searchQuery).ConfigureAwait(false);
                    return;
                case "GoogleWeb":
                    await HandleGoogleWebSearch(searchQuery).ConfigureAwait(false);
                    return;
                default:
                    await HandleApiSearch(searchQuery, token).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Unexpected error in SelectionDelayTimer_Tick.").ConfigureAwait(false);
        }
    }

    private void StatusMessageTimer_Tick(object? sender, EventArgs e)
    {
        _statusMessageTimer?.Stop();

        // Store and clear the pending message before releasing the lock
        var messageToApply = _pendingStatusMessage;
        _pendingStatusMessage = null;

        // Release the lock
        _isStatusMessageTimed = false;

        // Apply any pending message after releasing the lock
        if (messageToApply != null)
        {
            StatusMessageText = messageToApply;
        }
    }

    private async Task HandleApiSearch(string searchQuery, CancellationToken token)
    {
        try
        {
            // For API searches, we typically want exact matches, so quoting the search term is good.
            // Ensure any existing quotes are handled to avoid double quoting.
            var apiSearchQuery = $"\"{searchQuery.Replace("\"", "")}\"";
            AppLogger.Log($"Starting API image search for '{_selectedGameFileName}' using {Settings.SearchEngine}.");
            AppLogger.Log($"API Search query: {apiSearchQuery}");

            List<ImageData> coverImageUrls;
            try
            {
                coverImageUrls = await FetchImagesWithRetry(apiSearchQuery, Settings.SearchEngine, token).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("API Key is not set"))
            {
                _ = BugReport.LogErrorAsync(ex, "API Key is not set during image search.");
                await Dispatcher.InvokeAsync(static () =>
                {
                    MessageBox.Show("Please configure your API keys in Settings > API Settings.", "Missing API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                coverImageUrls = [];
            }
            catch (InvalidOperationException ex)
            {
                _ = BugReport.LogErrorAsync(ex, "API error during image search.");
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show(ex.Message, "API Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                coverImageUrls = [];
            }
            catch (OperationCanceledException)
            {
                AppLogger.Log("Image fetch operation was canceled.");
                await Dispatcher.InvokeAsync(() => { StatusMessageText = "Search canceled."; });
                return;
            }

            if (token.IsCancellationRequested) return;

            var thumbnailSize = Settings.ThumbnailSize;

            // Prepare all image data items in a temporary list first (off UI thread)
            List<ImageData> imageDataList;
            if (coverImageUrls.Count > 0)
            {
                AppLogger.Log($"Fetched {coverImageUrls.Count} images from {Settings.SearchEngine}.");
                imageDataList = coverImageUrls.Select(result =>
                {
                    result.ThumbnailWidth = thumbnailSize;
                    result.ThumbnailHeight = thumbnailSize;
                    return result;
                }).ToList();
            }
            else
            {
                AppLogger.Log($"No results found for query: {apiSearchQuery}");
                imageDataList =
                [
                    new ImageData
                    {
                        ImageName = "No Cover Image Found",
                        ImagePath = "pack://application:,,,/images/default.png",
                        ThumbnailWidth = thumbnailSize,
                        ThumbnailHeight = thumbnailSize
                    }
                ];
            }

            // Batch update the ObservableCollection by creating a new one (avoids individual CollectionChanged events)
            await Dispatcher.InvokeAsync(() =>
            {
                // Create new ObservableCollection to avoid triggering notifications for each item
                PanelImages = new ObservableCollection<ImageData>(imageDataList);
                OnPropertyChanged(nameof(PanelImages));

                StatusMessageText = coverImageUrls.Count > 0 ? $"Found {coverImageUrls.Count} images." : "No images found.";
                LblSearchQuery.Content = coverImageUrls.Count > 0
                    ? new TextBlock
                    {
                        Inlines =
                        {
                            new Run(apiSearchQuery) { FontWeight = FontWeights.Bold },
                            new Run($" (Fetched {coverImageUrls.Count} images from {Settings.SearchEngine})")
                        }
                    }
                    : new TextBlock(new Run($"{apiSearchQuery} (No results found from {Settings.SearchEngine})"));

                UpdateStatusBar();
                ImageScrollViewer.ScrollToTop();
            });
        }
        catch (OperationCanceledException ex)
        {
            AppLogger.Log("Image fetch operation was canceled.");
            _ = BugReport.LogErrorAsync(ex, "Image fetch operation was canceled in HandleApiSearch.");
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
                        Google.LoadApiKeyFromSettings(Settings);
                        return await googleProvider.FetchImagesFromGoogleAsync(searchQuery, Settings, cancellationToken).ConfigureAwait(false);
                    default:
                        return [];
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("API Key is not set") && retryCount < maxRetries)
            {
                retryCount++;
                _ = BugReport.LogErrorAsync(ex, "API Key is not set during FetchImagesWithRetry.");

                // Show API settings dialog
                var result = MessageBox.Show($"API Key is not set.\n\n" +
                                             $"Would you like to configure your API keys now?", "Missing API Key", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    var apiSettingsWindow = new ApiSettingsWindow(Settings)
                    {
                        Owner = this
                    };

                    if (apiSettingsWindow.ShowDialog() != true)
                    {
                        return []; // User cancelled
                    }
                    // Continue to retry
                }
                else
                {
                    return []; // User doesn't want to configure
                }
            }
            catch (InvalidOperationException ex)
            {
                _ = BugReport.LogErrorAsync(ex, "API error in FetchImagesWithRetry.");
                MessageBox.Show(ex.Message, "API Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return [];
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Handle cancellation gracefully - rethrow to let caller handle
                throw;
            }
        }

        return [];
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
        ContextMenuRemoveItem?.IsEnabled = hasSelection;

        ContextMenuCopyFileName?.IsEnabled = hasSelection;
    }

    private void SetThumbnailSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem ||
            !int.TryParse(menuItem.Header.ToString()?.Replace(" pixels", ""), out var size)) return;

        Settings.ThumbnailSize = size;
        Settings.SaveSettings();

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
        var savedSize = Settings.ThumbnailSize;
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
            item.IsChecked = item.Tag?.ToString() == Settings.SearchEngine;
        }
    }

    private void SetSearchEngine_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: not null } menuItem) return;

        try
        {
            Settings.SearchEngine = menuItem.Tag.ToString() ?? throw new InvalidOperationException();
            Settings.SaveSettings();
            UpdateMenuItems();
            AppLogger.Log($"Search engine set to '{Settings.SearchEngine}'.");
            StatusMessageText = $"Search engine set to {Settings.SearchEngine}.";
            SearchEngineDisplay = Settings.SearchEngine;

            UpdateSearchUiVisibility(); // Update visibility immediately
            PanelImages.Clear(); // Clear API results when switching to the web view
            WebView?.CoreWebView2?.Navigate("about:blank"); // Clear WebView content

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
        ToggleDebugWindow?.IsChecked = false;
    }

    private void ApiSettings_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("API Settings menu item clicked.");
        try
        {
            var apiSettingsWindow = new ApiSettingsWindow(Settings)
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

    private void EditExtensions_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(Settings)
        {
            Owner = this
        };
        settingsWindow.ShowDialog();
    }

    private void ToggleMameDescriptions_Click(object sender, RoutedEventArgs e)
    {
        Settings.UseMameDescriptions = ToggleMameDescriptions.IsChecked;
        Settings.SaveSettings();
        StatusMessageText = $"MAME descriptions turned {(ToggleMameDescriptions.IsChecked ? "on" : "off")}.";
        AppLogger.Log($"MAME descriptions turned {(ToggleMameDescriptions.IsChecked ? "on" : "off")}.");
    }

    private void ShowAboutWindow_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow().ShowDialog();
    }

    private static void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        // ForceClose sets the flag to allow the window to close properly
        // Close() is not needed as ForceClose() already handles the cleanup
        App.LogWindow?.ForceClose();
    }

    private async void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: ImageData imageData } ||
                string.IsNullOrEmpty(imageData.ImagePath) ||
                imageData.ImagePath.StartsWith("pack://", StringComparison.Ordinal))
            {
                return;
            }

            AppLogger.Log($"Save image clicked for image: {imageData.ImagePath}");

            if (string.IsNullOrEmpty(_imageFolderPath) || string.IsNullOrEmpty(_selectedGameFileName))
            {
                MessageBox.Show("Please select both an image folder and an item from the list.", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Sanitize filename to prevent path traversal attacks
            var safeFileName = SanitizeFileName(_selectedGameFileName);

            StatusMessageText = $"Saving image for '{safeFileName}'...";
            var localPath = Path.Combine(_imageFolderPath, safeFileName + ".png");
            AppLogger.Log($"Attempting to save image for '{safeFileName}' to '{localPath}'.");

            if (File.Exists(localPath))
            {
                var result = MessageBox.Show($"The file '{safeFileName}.png' already exists. Do you want to overwrite it?",
                    "File Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    AppLogger.Log("User chose not to overwrite existing file.");
                    StatusMessageText = "Save canceled by user.";
                    return;
                }

                AppLogger.Log("User chose to overwrite existing file.");
            }

            try
            {
                var success = await App.ImageSaveService.DownloadAndSaveImageAsync(imageData.ImagePath, localPath).ConfigureAwait(false);

                if (success)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        RemoveSelectedItem();
                        PlaySound.PlayClickSound();
                        StatusMessageText = $"Image saved: {safeFileName}.png";
                    });
                }
                else
                {
                    StatusMessageText = "Failed to download or save image.";
                }
            }
            catch (Exception ex)
            {
                StatusMessageText = "Error saving image.";
                _ = BugReport.LogErrorAsync(ex, "Error saving image.");
                MessageBox.Show("There was an error saving the image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Error in SaveImage_Click outer try-catch.").ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        AppLogger.Log("Disposing MainWindow resources.");
        _searchCts?.Dispose();

        // Unsubscribe timer event handlers to prevent memory leaks
        if (_selectionDelayTimer != null)
        {
            _selectionDelayTimer.Tick -= SelectionDelayTimer_Tick;
            _selectionDelayTimer.Stop();
            _selectionDelayTimer = null;
        }

        if (_statusMessageTimer != null)
        {
            _statusMessageTimer.Tick -= StatusMessageTimer_Tick;
            _statusMessageTimer.Stop();
            _statusMessageTimer = null;
        }

        // Unsubscribe WebView event handler to prevent memory leaks
        if (WebView != null)
        {
            WebView.NavigationCompleted -= WebView_NavigationCompleted;
        }

        _imageFolderWatcher?.Dispose();
        WebView?.Dispose();
        GC.SuppressFinalize(this);
    }

    // Matches parenthesized or bracketed text: (content) or [content]
    [GeneratedRegex(@"\s*(\(.*?\)|\[.*?\])")]
    private static partial Regex MyRegex();
}
