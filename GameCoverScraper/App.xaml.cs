using System.Windows;
using ControlzEx.Theming;
using GameCoverScraper.Managers;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class App
{
    public static DebugWindow? LogWindow { get; private set; }

    private static string? StartupImageFolder { get; set; }
    private static string? StartupRomFolder { get; set; }

    protected override void OnStartup(StartupEventArgs e)
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

        // Apply theme from settings
        ThemeManager.Current.ChangeTheme(Current, $"{settings.BaseTheme}.{settings.AccentColor}");
        AppLogger.Log($"Theme set to {settings.BaseTheme}.{settings.AccentColor}.");

        // Create and show the main window, passing the parsed startup arguments
        var mainWindow = new MainWindow(StartupImageFolder, StartupRomFolder);
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Log("Application exiting.");
        HttpClientHelper.Dispose(); // Clean up the shared HttpClient
        base.OnExit(e);
    }
}
