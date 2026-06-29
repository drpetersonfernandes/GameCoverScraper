using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameCoverScraper.Managers;
using GameCoverScraper.Models;
using GameCoverScraper.Services;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace GameCoverScraper;

public partial class MainWindow : INotifyPropertyChanged, IDisposable
{
    private CancellationTokenSource? _loadMissingCts;
    private Dictionary<string, string>? _mameLookup;
    private CancellationTokenSource? _findSimilarCts;
    private readonly SemaphoreSlim _findSimilarSemaphore = new(1, 1);
    private string _selectedRomFileName = string.Empty;
    private bool _disposed;
    private CoreWebView2Environment? _webViewEnv;
    private ImageFolderWatcher? _imageFolderWatcher;
    private SystemTrayIcon? _systemTrayIcon;
    private bool _isMinimizedToTray;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ImageData> SimilarImages { get; set; } = [];
    public ObservableCollection<ImageData> PanelImages { get; set; } = [];
    public ObservableCollection<MissingImageItem> MissingImages { get; set; } = [];

    public bool IsCheckingMissing
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnPropertyChanged();
        }
    }

    public bool IsFindingSimilar
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnPropertyChanged();
        }
    }

    public bool HasSearchedSimilar
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnPropertyChanged();
        }
    }

    public bool IsSearching
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnPropertyChanged();
        }
    }

    public bool HasSearchedApi
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnPropertyChanged();
        }
    }

    public SettingsManager Settings { get; }

    public ICommand CheckForMissingImagesCommand { get; }
    public ICommand ExitCommand { get; }

    public MainWindow(SettingsManager settingsManager, string? startupImageFolder = null, string? startupRomFolder = null)
    {
        Settings = settingsManager;
        InitializeComponent();
        DataContext = this;

        CheckForMissingImagesCommand = new DelegateCommand(
            _ => BtnCheckForMissingImages_ClickAsync(this, new RoutedEventArgs()),
            _ => BtnCheckForMissingImages?.IsEnabled ?? false);
        ExitCommand = new DelegateCommand(_ => Close());

        if (!string.IsNullOrEmpty(startupImageFolder))
        {
            TxtImageFolder.Text = startupImageFolder;
        }

        if (!string.IsNullOrEmpty(startupRomFolder))
        {
            TxtRomFolder.Text = startupRomFolder;
        }

        UpdateThumbnailSizeMenuChecks();
        UpdateSimilarityAlgorithmChecks();
        UpdateSimilarityThresholdChecks();
        UpdateAccentColorChecks();
        UpdateBaseThemeMenuChecks();
        UpdateMameDescriptionCheck();

        Settings.PropertyChanged += AppSettingsManagerPropertyChangedAsync;
        Closing += OnWindowClosing;
        Loaded += MainWindow_LoadedAsync;
        StateChanged += OnWindowStateChanged;

        InitializeNotifyIcon();

        LoadMameData();
        UpdateUiStateForFolderPaths();
    }

    private void InitializeNotifyIcon()
    {
        _systemTrayIcon = new SystemTrayIcon();
        _systemTrayIcon.Initialize();
        _systemTrayIcon.RestoreRequested += RestoreFromTray;
        _systemTrayIcon.ExitRequested += ExitApplication;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            MinimizeToTray();
        }
    }

    private void MinimizeToTray()
    {
        _isMinimizedToTray = true;
        _systemTrayIcon!.Visible = true;
        Hide();
        _systemTrayIcon.ShowBalloonTip("GameCoverScraper", "Application minimized to tray.");
    }

    private void RestoreFromTray()
    {
        _isMinimizedToTray = false;
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _systemTrayIcon!.Visible = false;
    }

    private void ExitApplication()
    {
        _systemTrayIcon?.Dispose();
        _systemTrayIcon = null;
        Application.Current.Shutdown();
    }

    private async void MainWindow_LoadedAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            LstMissingImages.PreviewKeyDown += LstMissingImages_PreviewKeyDown;

            await InitializeWebViewsAsync();

            if (!string.IsNullOrEmpty(TxtRomFolder.Text) && !string.IsNullOrEmpty(TxtImageFolder.Text))
            {
                await RefreshMissingImagesListAsync();
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in MainWindow_LoadedAsync");
        }
    }

    private async Task InitializeWebViewsAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GameCoverScraper", "WebView2Data");

            try
            {
                if (!Directory.Exists(userDataFolder))
                    Directory.CreateDirectory(userDataFolder);
            }
            catch
            {
                userDataFolder = null;
            }

            _webViewEnv = await CoreWebView2Environment.CreateAsync(null, userDataFolder);

            try
            {
                await GoogleWebView.EnsureCoreWebView2Async(_webViewEnv);
                GoogleWebView.NavigationCompleted += (_, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        IsSearching = false;
                        StatusMessage.Text = "Google web search loaded.";
                    });
                };
            }
            catch (Exception ex)
            {
                _ = ErrorLogger.LogAsync(ex, "Failed to initialize Google WebView2");
            }

            try
            {
                await BingWebView.EnsureCoreWebView2Async(_webViewEnv);
                BingWebView.NavigationCompleted += (_, _) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        IsSearching = false;
                        StatusMessage.Text = "Bing web search loaded.";
                    });
                };
            }
            catch (Exception ex)
            {
                _ = ErrorLogger.LogAsync(ex, "Failed to initialize Bing WebView2");
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Failed to initialize WebView2 environment");
        }
    }

    private async Task<bool> EnsureWebViewReadyAsync(Microsoft.Web.WebView2.Wpf.WebView2 webView)
    {
        if (webView.CoreWebView2 != null)
            return true;

        try
        {
            if (_webViewEnv == null)
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GameCoverScraper", "WebView2Data");

                try
                {
                    if (!Directory.Exists(userDataFolder))
                        Directory.CreateDirectory(userDataFolder);
                }
                catch
                {
                    userDataFolder = null;
                }

                _webViewEnv = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            }

            await webView.EnsureCoreWebView2Async(_webViewEnv);
            return webView.CoreWebView2 != null;
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Failed to lazily initialize WebView2");
            return false;
        }
    }

    private void LoadMameData()
    {
        try
        {
            var machines = MameManager.LoadFromDat();
            _mameLookup = machines
                .GroupBy(static m => m.MachineName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static g => g.Key, static g => g.First().Description, StringComparer.OrdinalIgnoreCase);
            ToggleMameDescriptions.IsEnabled = true;
        }
        catch (Exception ex)
        {
            _mameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ToggleMameDescriptions.IsEnabled = false;
            ToggleMameDescriptions.IsChecked = false;
            _ = ErrorLogger.LogAsync(ex, "Failed to load MAME data");
        }
    }

    private async void AppSettingsManagerPropertyChangedAsync(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            switch (e.PropertyName)
            {
                case nameof(SettingsManager.BaseTheme):
                    UpdateBaseThemeMenuChecks();
                    break;
                case nameof(SettingsManager.AccentColor):
                    UpdateAccentColorChecks();
                    break;
                case nameof(SettingsManager.ImageWidth):
                case nameof(SettingsManager.ImageHeight):
                    UpdateThumbnailSizeMenuChecks();
                    break;
                case nameof(SettingsManager.SelectedSimilarityAlgorithm):
                    UpdateSimilarityAlgorithmChecks();
                    break;
                case nameof(SettingsManager.SimilarityThreshold):
                    UpdateSimilarityThresholdChecks();
                    break;
                case nameof(SettingsManager.UseMameDescriptions):
                    UpdateMameDescriptionCheck();
                    if (_mameLookup is { Count: > 0 })
                        await RefreshMissingImagesListAsync();
                    break;
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in AppSettings_PropertyChanged");
        }
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void ChangeBaseTheme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        var theme = menuItem.Name == "LightTheme" ? "Light" : "Dark";
        App.ChangeTheme(theme, Settings.AccentColor);
    }

    private void ChangeAccentColor_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        var accent = menuItem.Name.Replace("Accent", "");
        App.ChangeTheme(Settings.BaseTheme, accent);
    }

    private void UpdateAccentColorChecks()
    {
        var currentAccent = Settings.AccentColor;
        foreach (var item in MenuAccentColors.Items)
        {
            if (item is not MenuItem { Header: not null } menuItem) continue;

            if (menuItem.Header is StackPanel sp)
            {
                var tb = sp.Children.OfType<TextBlock>().FirstOrDefault();
                if (tb != null)
                {
                    menuItem.IsChecked = tb.Text.Replace("Accent", "") == currentAccent;
                }
            }
            else
            {
                menuItem.IsChecked = menuItem.Name.Replace("Accent", "") == currentAccent;
            }
        }
    }

    private void UpdateBaseThemeMenuChecks()
    {
        LightTheme.IsChecked = Settings.BaseTheme == "Light";
        DarkTheme.IsChecked = Settings.BaseTheme == "Dark";
    }

    private void UpdateMameDescriptionCheck()
    {
        ToggleMameDescriptions.IsChecked = Settings.UseMameDescriptions;
    }

    private void DonateButton_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.purelogiccode.com/donate") { UseShellExecute = true }); }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to open the donation link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = ErrorLogger.LogAsync(ex, "Error opening donation link");
        }
    }

    private void ShowAboutWindow_Click(object sender, RoutedEventArgs e)
    {
        new AboutWindow { Owner = this }.ShowDialog();
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var updateInfo = await UpdateCheckService.CheckForUpdateAsync();

            if (updateInfo is { IsUpdateAvailable: true })
            {
                var choice = MessageBox.Show(
                    $"A new version of GameCoverScraper is available!\n\n" +
                    $"Current: {updateInfo.CurrentVersion}\n" +
                    $"Latest: {updateInfo.LatestVersion}\n\n" +
                    "Would you like to go to the download page?",
                    "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                if (choice == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updateInfo.ReleaseUrl) { UseShellExecute = true });
                }
            }
            else
            {
                MessageBox.Show(
                    $"You are running the latest version (v{updateInfo.CurrentVersion}).",
                    "No Updates Available", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unable to check for updates: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = ErrorLogger.LogAsync(ex, "Error checking for updates");
        }
    }

    private void ApiSettings_Click(object sender, RoutedEventArgs e)
    {
        new ApiSettingsWindow(Settings) { Owner = this }.ShowDialog();
    }

    private void ToggleDebugWindow_Click(object sender, RoutedEventArgs e)
    {
        if (App.LogWindow == null) return;

        if (ToggleDebugWindow.IsChecked) App.LogWindow.Show(); else App.LogWindow.Hide();
    }

    private void ToggleMameDescriptions_Click(object sender, RoutedEventArgs e)
    {
        Settings.UseMameDescriptions = ToggleMameDescriptions.IsChecked;
        Settings.SaveSettings();
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isMinimizedToTray)
        {
            // Allow close when exiting from tray - cancel running operations
            try { _findSimilarCts?.Cancel(); }
            catch
            {
                // ignored
            }

            try { _loadMissingCts?.Cancel(); }
            catch
            {
                // ignored
            }

            return;
        }

        // Minimize to tray instead of closing
        e.Cancel = true;
        MinimizeToTray();
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void BtnBrowseRomFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder where your ROM or ISO files are stored." };
        if (dialog.ShowDialog() == true)
        {
            TxtRomFolder.Text = dialog.FolderName;
            UpdateUiStateForFolderPaths();
        }
    }

    private void BtnBrowseImageFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select the folder where your image files are stored." };
        if (dialog.ShowDialog() != true) return;

        TxtImageFolder.Text = dialog.FolderName;
        UpdateUiStateForFolderPaths();
        Settings.LastImageFolder = dialog.FolderName;
        Settings.SaveSettings();
    }

    private async void BtnCheckForMissingImages_ClickAsync(object sender, RoutedEventArgs e)
    {
        try { await RefreshMissingImagesListAsync(); }
        catch (Exception ex) { _ = ErrorLogger.LogAsync(ex, "Error in BtnCheckForMissingImages_Click"); }
    }

    private async Task RefreshMissingImagesListAsync()
    {
        if (_loadMissingCts != null)
        {
            await _loadMissingCts.CancelAsync();
            _loadMissingCts.Dispose();
            _loadMissingCts = null;
        }

        var cts = new CancellationTokenSource();
        _loadMissingCts = cts;
        try { await LoadMissingImagesListAsync(cts.Token); }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) { _ = ErrorLogger.LogAsync(ex, "Error refreshing missing images list."); }
        finally
        {
            cts.Dispose();
            if (_loadMissingCts == cts)
            {
                _loadMissingCts = null;
            }
        }
    }

    private async Task LoadMissingImagesListAsync(CancellationToken cancellationToken = default)
    {
        if (Settings.SupportedExtensions.Length == 0)
        {
            MessageBox.Show("No supported file extensions loaded. Please check settings.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var romFolderPath = GetValidatedRomFolderPath();
        var imageFolderPath = GetValidatedImageFolderPath();

        if (string.IsNullOrEmpty(romFolderPath) || string.IsNullOrEmpty(imageFolderPath))
        {
            MessageBox.Show("Please select both ROM and Image folders.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsCheckingMissing = true;
        try
        {
            var missingFiles = await Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var allRomNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var supportedExtensionsSet = new HashSet<string>(
                    Settings.SupportedExtensions.Select(static ext => "." + ext), StringComparer.OrdinalIgnoreCase);

                var files = Directory.EnumerateFiles(romFolderPath, "*.*", new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = true });

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (supportedExtensionsSet.Contains(Path.GetExtension(file)))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (!string.IsNullOrEmpty(fileName)) allRomNames.Add(fileName);
                    }
                }

                var missing = new List<(string RomName, string SearchName)>();
                var processedCount = 0;
                foreach (var romName in allRomNames)
                {
                    if (++processedCount % 100 == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }

                    if (FindCorrespondingImage(romName, imageFolderPath) == null)
                    {
                        if (Settings.UseMameDescriptions && _mameLookup != null &&
                            _mameLookup.TryGetValue(romName, out var description) && !string.IsNullOrEmpty(description))
                            missing.Add((romName, description));
                        else
                            missing.Add((romName, romName));
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                return missing.OrderBy(static x => x.RomName).ToList();
            }, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            MissingImages.Clear();
            SimilarImages.Clear();

            foreach (var item in missingFiles.Select(static mf => new MissingImageItem(mf.RomName, mf.SearchName)))
                MissingImages.Add(item);

            UpdateMissingCount();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error checking for missing images: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = ErrorLogger.LogAsync(ex, "Error checking for missing images");
        }
        finally { IsCheckingMissing = false; }
    }

    private static string? FindCorrespondingImage(string fileNameWithoutExtension, string imageFolderPath)
    {
        foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            var imagePath = Path.Combine(imageFolderPath, fileNameWithoutExtension + ext);
            if (File.Exists(imagePath)) return imagePath;
        }

        return null;
    }

    private void RemoveSelectedItem(int? index = null)
    {
        var removeIndex = index ?? LstMissingImages.SelectedIndex;
        if (removeIndex < 0 || removeIndex >= MissingImages.Count) return;

        try
        {
            MissingImages.RemoveAt(removeIndex);
            if (MissingImages.Count > 0)
            {
                var newIndex = Math.Min(removeIndex, MissingImages.Count - 1);
                LstMissingImages.SelectedIndex = newIndex;
                LstMissingImages.ScrollIntoView(MissingImages[newIndex]);
            }
            else { LblLocalSearchQuery.Content = null; }
        }
        catch (Exception ex) { _ = ErrorLogger.LogAsync(ex, "Error in RemoveSelectedItem"); }

        UpdateMissingCount();
    }

    private void UpdateMissingCount()
    {
        try { LabelMissingRoms.Content = AppConstants.Messages.MissingCoversPrefix + MissingImages.Count; }
        catch (Exception ex) { _ = ErrorLogger.LogAsync(ex, "Error in UpdateMissingCount"); }
    }

    private void BtnRemoveSelectedItem_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedItem();
        SimilarImages.Clear();
        App.AudioService.PlayClickSound();
    }

    private void RemoveItemFromList_Click(object sender, RoutedEventArgs e)
    {
        RemoveSelectedItem();
        App.AudioService.PlayClickSound();
    }

    private void CopyFileName_Click(object sender, RoutedEventArgs e)
    {
        if (LstMissingImages.SelectedItem is MissingImageItem item)
        {
            try { Clipboard.SetText(item.RomName); }
            catch
            {
                // ignored
            }
        }
    }

    private void DeleteCorrespondingRom_Click(object sender, RoutedEventArgs e)
    {
        if (LstMissingImages.SelectedItem is not MissingImageItem selectedItem) return;

        var romFolderPath = GetValidatedRomFolderPath();
        if (string.IsNullOrEmpty(romFolderPath)) return;

        var romFilePath = FindCorrespondingRomFile(selectedItem.RomName, romFolderPath);
        if (romFilePath == null)
        {
            MessageBox.Show($"Could not find a ROM or ISO file for '{selectedItem.RomName}' in the ROM folder.",
                "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Are you sure you want to permanently delete this file?\n\n{romFilePath}\n\nThis action cannot be undone.",
            "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            File.Delete(romFilePath);
            AppLogger.Log($"Deleted ROM file: {romFilePath}");
            RemoveSelectedItem();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete file: {ex.Message}", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = ErrorLogger.LogAsync(ex, $"Error deleting ROM file: {romFilePath}");
        }
    }

    private static string? FindCorrespondingRomFile(string fileNameWithoutExtension, string romFolderPath)
    {
        var supportedExtensions = new[] { ".iso", ".zip", ".7z", ".rar", ".chd", ".cue", ".bin", ".gcz", ".rvz", ".wbf", ".wbfs", ".nds", ".3ds", ".cia", ".gba", ".gbc", ".nes", ".sfc", ".smc", ".md", ".gen", ".n64", ".z64", ".v64" };

        foreach (var ext in supportedExtensions)
        {
            var romPath = Path.Combine(romFolderPath, fileNameWithoutExtension + ext);
            if (File.Exists(romPath)) return romPath;
        }

        // Try case-insensitive search
        if (!Directory.Exists(romFolderPath)) return null;

        foreach (var file in Directory.EnumerateFiles(romFolderPath, "*.*", SearchOption.AllDirectories))
        {
            if (string.Equals(Path.GetFileNameWithoutExtension(file), fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase))
            {
                return file;
            }
        }

        return null;
    }

    private void EditExtensions_Click(object sender, RoutedEventArgs e)
    {
        try { new SettingsWindow(Settings) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { _ = ErrorLogger.LogAsync(ex, "Error in EditExtensions_Click"); }
    }

    private void SetSimilarityAlgorithm_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;

        Settings.SelectedSimilarityAlgorithm = menuItem.Header.ToString() ?? "Jaro-Winkler Distance";
        Settings.SaveSettings();
    }

    private void UpdateSimilarityAlgorithmChecks()
    {
        foreach (var item in MenuSimilarityAlgorithms.Items)
        {
            if (item is MenuItem menuItem)
            {
                menuItem.IsChecked = menuItem.Header.ToString() == Settings.SelectedSimilarityAlgorithm;
            }
        }
    }

    private void SetSimilarityThreshold_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem clickedItem) return;

        var headerText = clickedItem.Header.ToString()?.Replace("%", "") ?? "70";
        if (double.TryParse(headerText, out var rate))
        {
            Settings.SimilarityThreshold = rate;
            Settings.SaveSettings();
        }
    }

    private void UpdateSimilarityThresholdChecks()
    {
        var currentThreshold = Settings.SimilarityThreshold;
        foreach (var item in MySimilarityMenu.Items)
        {
            if (item is not MenuItem menuItem) continue;

            var thresholdString = menuItem.Header.ToString()?.Replace("%", "") ?? "70";
            if (double.TryParse(thresholdString, NumberStyles.Any, CultureInfo.InvariantCulture, out var menuItemThreshold))
            {
                menuItem.IsChecked = Math.Abs(menuItemThreshold - currentThreshold) < 0.001;
            }
        }
    }

    private void SetThumbnailSize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        if (menuItem.Tag is not int size && !int.TryParse(menuItem.Tag?.ToString(), out size)) return;

        Settings.ImageWidth = size;
        Settings.ImageHeight = size;
        Settings.SaveSettings();
    }

    private void UpdateThumbnailSizeMenuChecks()
    {
        var currentWidth = Settings.ImageWidth;
        foreach (var item in ImageSizeMenu.Items)
        {
            if (item is not MenuItem menuItem) continue;

            if (menuItem.Tag is int size || int.TryParse(menuItem.Tag?.ToString(), out size))
            {
                menuItem.IsChecked = size == currentWidth;
            }
        }
    }

    private void LstMissingImages_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e is { Key: Key.Delete, IsRepeat: false })
        {
            RemoveSelectedItem();
            App.AudioService.PlayClickSound();
            e.Handled = true;
        }
    }

    private void TxtRomFolder_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUiStateForFolderPaths();
    }

    private void TxtImageFolder_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateUiStateForFolderPaths();
    }

    private void TxtRomFolder_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateUiStateForFolderPaths();
    }

    private void TxtImageFolder_LostFocus(object sender, RoutedEventArgs e)
    {
        UpdateUiStateForFolderPaths();
    }

    private void TxtRomFolder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UpdateUiStateForFolderPaths();
            e.Handled = true;
        }
    }

    private void TxtImageFolder_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            UpdateUiStateForFolderPaths();
            e.Handled = true;
        }
    }

    private string? GetValidatedImageFolderPath(bool showWarning = true)
    {
        return ValidateFolderPath(TxtImageFolder.Text.Trim(), "Image", showWarning);
    }

    private string? GetValidatedRomFolderPath(bool showWarning = true)
    {
        return ValidateFolderPath(TxtRomFolder.Text.Trim(), "ROM", showWarning);
    }

    private static string? ValidateFolderPath(string path, string folderType, bool showWarning)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Directory.Exists(path)) return path;

        if (showWarning) MessageBox.Show($"The {folderType.ToLowerInvariant()} folder path '{path}' is invalid or does not exist.", $"Invalid {folderType} Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        return null;
    }

    private void UpdateUiStateForFolderPaths()
    {
        try
        {
            var romPathValid = !string.IsNullOrEmpty(TxtRomFolder.Text.Trim()) && Directory.Exists(TxtRomFolder.Text.Trim());
            var imagePathValid = !string.IsNullOrEmpty(TxtImageFolder.Text.Trim()) && Directory.Exists(TxtImageFolder.Text.Trim());
            BtnCheckForMissingImages.IsEnabled = romPathValid && imagePathValid;
            LstMissingImages.IsEnabled = romPathValid && imagePathValid;
            if (!romPathValid || !imagePathValid)
            {
                MissingImages.Clear();
                SimilarImages.Clear();
                UpdateMissingCount();
            }

            StartImageFolderWatcher(imagePathValid ? TxtImageFolder.Text.Trim() : null);

            CommandManager.InvalidateRequerySuggested();
        }
        catch (Exception ex) { _ = ErrorLogger.LogAsync(ex, "Error in UpdateUiStateForFolderPaths"); }
    }

    private void StartImageFolderWatcher(string? folderPath)
    {
        _imageFolderWatcher?.Stop();

        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            AppLogger.Log($"StartImageFolderWatcher: skipped (path='{folderPath}', exists={Directory.Exists(folderPath ?? "")})");
            return;
        }

        _imageFolderWatcher ??= new ImageFolderWatcher();
        _imageFolderWatcher.ImageFound -= OnImageFolderImageFound;
        _imageFolderWatcher.ImageFound += OnImageFolderImageFound;
        _imageFolderWatcher.Start(folderPath);
        AppLogger.Log($"StartImageFolderWatcher: watcher started for '{folderPath}'");
    }

    private void OnImageFolderImageFound(string fileNameWithoutExtension)
    {
        Dispatcher.Invoke(() =>
        {
            var index = MissingImages.ToList().FindIndex(m =>
                string.Equals(m.RomName, fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));

            if (index < 0) return;

            RemoveSelectedItem(index);
            _imageFolderWatcher?.PendingRenameTarget = null;
            AppLogger.Log($"Auto-removed '{fileNameWithoutExtension}' from missing images.");
        });
    }

    private void SearchTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (StatusSearchEngine == null) return;

        var tab = SearchTabControl.SelectedIndex;
        StatusSearchEngine.Text = tab switch
        {
            0 => "Local Files",
            1 => "Google Web",
            2 => "Bing Web",
            3 => "Google API",
            _ => "Unknown"
        };

        // If an item is already selected, trigger the search for the new tab
        if (LstMissingImages.SelectedItem is MissingImageItem)
        {
            LstMissingImages_SelectionChanged(LstMissingImages, new SelectionChangedEventArgs(
                System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                Array.Empty<object>(), Array.Empty<object>()));
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Settings.PropertyChanged -= AppSettingsManagerPropertyChangedAsync;
        _findSimilarCts?.Cancel();
        _loadMissingCts?.Cancel();
        _findSimilarCts?.Dispose();
        _loadMissingCts?.Dispose();
        _findSimilarSemaphore.Dispose();
        _imageFolderWatcher?.Dispose();
        _systemTrayIcon?.Dispose();
        GC.SuppressFinalize(this);
    }
}
