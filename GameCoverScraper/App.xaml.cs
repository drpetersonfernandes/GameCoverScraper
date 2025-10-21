using System.Windows;
using ControlzEx.Theming;
using GameCoverScraper.Managers;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class App
{
    public static DebugWindow? LogWindow { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Register SixLabors.ImageSharp decoders for various formats
        AppLogger.Log("SixLabors.ImageSharp decoders registered for WebP, TIFF, and AVIF.");

        LogWindow = new DebugWindow();
        AppLogger.Log("Application starting up.");

        var settings = new SettingsManager();
        settings.LoadSettings();

        // Initialize BugReport with settings
        BugReport.Initialize(settings);

        // Apply theme from settings
        ThemeManager.Current.ChangeTheme(Current, $"{settings.BaseTheme}.{settings.AccentColor}");
        AppLogger.Log($"Theme set to {settings.BaseTheme}.{settings.AccentColor}.");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Log("Application exiting.");
        HttpClientHelper.Dispose(); // Clean up the shared HttpClient
        base.OnExit(e);
    }
}