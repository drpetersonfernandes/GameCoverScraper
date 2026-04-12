using System.Windows;
using ControlzEx.Theming;
using GameCoverScraper.Managers;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class App
{
    public static DebugWindow? LogWindow { get; private set; }
    public static ImageSaveService ImageSaveService { get; } = new();
    public static WebSearchService WebSearchService { get; } = new();

    private static string? StartupImageFolder { get; set; }
    private static string? StartupRomFolder { get; set; }
    private static bool _statsRecorded;

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            switch (e.Args.Length)
            {
                // Parse command-line arguments
                case >= 2:
                    StartupImageFolder = e.Args[0];
                    StartupRomFolder = e.Args[1];
                    AppLogger.Log($"Startup arguments detected: ImageFolder='{StartupImageFolder}', RomFolder='{StartupRomFolder}'");
                    break;
                case 1:
                    // If only one argument is provided, assume it's the image folder
                    StartupImageFolder = e.Args[0];
                    AppLogger.Log($"Startup argument detected: ImageFolder='{StartupImageFolder}' (RomFolder missing)");
                    break;
                default:
                    AppLogger.Log("No startup arguments detected.");
                    break;
            }

            LogWindow = new DebugWindow();
            AppLogger.Log("Application starting up.");

            var settings = new SettingsManager();
            settings.LoadSettings();

            // Initialize BugReport with settings
            BugReport.Initialize(settings);

            // Record application stats to the stats API (only once per session)
            RecordStartupStatsOnce();

            // Apply theme from settings
            ThemeManager.Current.ChangeTheme(Current, $"{settings.BaseTheme}.{settings.AccentColor}");
            AppLogger.Log($"Theme set to {settings.BaseTheme}.{settings.AccentColor}.");

            // Create and show the main window, passing the parsed startup arguments
            var mainWindow = new MainWindow(StartupImageFolder, StartupRomFolder);
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            // Log critical startup error
            AppLogger.Log($"CRITICAL STARTUP ERROR: {ex.Message}");

            // Try to report the bug if BugReport is initialized
            try
            {
                _ = BugReport.LogErrorAsync(ex, "Critical error during application startup.");
            }
            catch
            {
                // If BugReport fails, we can't do much at this point
            }

            MessageBox.Show(
                $"A critical error occurred during application startup:\n\n{ex.Message}\n\nThe application will now close.",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            // Re-throw to allow the application to terminate normally
            throw;
        }
    }

    private static void RecordStartupStatsOnce()
    {
        if (_statsRecorded)
        {
            AppLogger.Log("Application stats already recorded for this session. Skipping duplicate call.");
            return;
        }

        _statsRecorded = true;
        _ = ApplicationStatsService.RecordStartupAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Log("Application exiting.");

        // Dispose the DebugWindow if it exists
        if (LogWindow != null)
        {
            LogWindow.ForceClose();
            LogWindow.Close();
            LogWindow = null;
        }

        HttpClientHelper.Dispose(); // Clean up the shared HttpClient
        base.OnExit(e);
    }
}
