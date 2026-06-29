using System.Diagnostics;
using System.IO;
using System.Windows;
using ControlzEx.Theming;
using GameCoverScraper.Managers;
using GameCoverScraper.Services;
using ImageMagick;
using Microsoft.Extensions.DependencyInjection;

namespace GameCoverScraper;

public partial class App
{
    public static IServiceProvider? ServiceProvider { get; private set; }

    public static SettingsManager SettingsManager
    {
        get
        {
            if (ServiceProvider != null) return ServiceProvider.GetRequiredService<SettingsManager>();

            throw new InvalidOperationException("ServiceProvider has not been initialized.");
        }
    }

    public static DebugWindow? LogWindow { get; private set; }
    public static ImageSaveService ImageSaveService { get; } = new();

    public static string? StartupImageFolderPath { get; private set; }
    public static string? StartupRomFolderPath { get; private set; }

    private static readonly Lazy<IAudioService> AudioServiceLazy = new(static () =>
    {
        try
        {
            return new LocalAudioService();
        }
        catch
        {
            return new NullAudioService();
        }
    });

    public static IAudioService AudioService => AudioServiceLazy.Value;

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<SettingsManager>();
    }

    private static void FireAndForget(Func<Task> asyncAction)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await asyncAction().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"FireAndForget caught unhandled exception: {ex}");
            }
        });
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception ?? new InvalidOperationException(args.ExceptionObject.ToString() ?? "Unknown AppDomain exception");
            _ = ErrorLogger.LogAsync(ex, "Unhandled AppDomain exception - Application will terminate");
            Current?.Dispatcher.BeginInvoke(static () =>
            {
                MessageBox.Show(
                    "An unexpected error occurred and the application needs to close.\n\nPlease report this issue to the development team.",
                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            _ = ErrorLogger.LogAsync(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        Current.DispatcherUnhandledException += (sender, args) =>
        {
            _ = ErrorLogger.LogAsync(args.Exception, "Unhandled dispatcher exception (UI Thread)");
            MessageBox.Show(
                "An unexpected error occurred.\n\n" +
                $"Error: {args.Exception.Message}\n\n" +
                "The error has been reported to the development team.",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            RegisterGlobalExceptionHandlers();

            var services = new ServiceCollection();
            ConfigureServices(services);
            ServiceProvider = services.BuildServiceProvider();

            FireAndForget(static () => ApplicationStatsService.RecordStartupAsync());

            ResourceLimits.Memory = AppConstants.DefaultMemoryLimit;
            ResourceLimits.Thread = AppConstants.DefaultThreadLimit;

            switch (e.Args.Length)
            {
                case >= 2:
                    {
                        var imageFolderPath = e.Args[0];
                        var romFolderPath = e.Args[1];

                        if (Directory.Exists(imageFolderPath) && Directory.Exists(romFolderPath))
                        {
                            StartupImageFolderPath = imageFolderPath;
                            StartupRomFolderPath = romFolderPath;
                        }

                        break;
                    }
                case 1:
                    {
                        if (Directory.Exists(e.Args[0]))
                        {
                            StartupImageFolderPath = e.Args[0];
                        }

                        break;
                    }
            }

            LogWindow = new DebugWindow();

            var settings = SettingsManager;

            var folderToClean = !string.IsNullOrEmpty(StartupImageFolderPath)
                ? StartupImageFolderPath
                : settings.LastImageFolder;

            if (!string.IsNullOrEmpty(folderToClean) && Directory.Exists(folderToClean))
            {
                await Task.Run(() => ImageProcessor.CleanupOrphanedTempFiles(folderToClean));
            }

            ApplyTheme(settings.BaseTheme, settings.AccentColor);

            var mainWindow = new MainWindow(SettingsManager, StartupImageFolderPath, StartupRomFolderPath);
            mainWindow.Show();

            FireAndForget(CheckForUpdatesAsync);
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in the method OnStartup");
            MessageBox.Show(
                $"A fatal error occurred during application startup:\n\n{ex.Message}\n\nThe application will now close.",
                "Fatal Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var updateInfo = await UpdateCheckService.CheckForUpdateAsync();
            if (updateInfo is { IsUpdateAvailable: true })
            {
                Current.Dispatcher.Invoke(() =>
                {
                    var choice = MessageBox.Show(
                        $"A new version of GameCoverScraper is available!\n\n" +
                        $"Current: {updateInfo.CurrentVersion}\n" +
                        $"Latest: {updateInfo.LatestVersion}\n\n" +
                        "Would you like to download it?",
                        "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (choice == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = updateInfo.ReleaseUrl,
                            UseShellExecute = true
                        });
                    }
                });
            }
        }
        catch
        {
            // Silently ignore update check failures
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { if (AudioService is IDisposable disposableAudioService) disposableAudioService.Dispose(); }
        catch
        {
            // ignored
        }

        try { ErrorLogger.Dispose(); }
        catch
        {
            // ignored
        }

        try { HttpClientHelper.Dispose(); }
        catch
        {
            // ignored
        }

        try
        {
            if (LogWindow != null)
            {
                LogWindow.ForceClose();
                LogWindow.Close();
                LogWindow = null;
            }
        }
        catch
        {
            // ignored
        }

        try { base.OnExit(e); }
        catch
        {
            // ignored
        }

        Environment.Exit(e.ApplicationExitCode);
    }

    public static void ChangeTheme(string baseTheme, string accentColor)
    {
        ApplyTheme(baseTheme, accentColor);
        SettingsManager.BaseTheme = baseTheme;
        SettingsManager.AccentColor = accentColor;
        SettingsManager.SaveSettings();
    }

    private static void ApplyTheme(string baseTheme, string accentColor)
    {
        try
        {
            ThemeManager.Current.ChangeTheme(Current, $"{baseTheme}.{accentColor}");
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            FireAndForget(() => ErrorLogger.LogAsync(ex, $"Error applying theme: {baseTheme}.{accentColor}"));
        }
    }

    public static void ApplyThemeToWindow(Window window)
    {
        try
        {
            var baseTheme = SettingsManager.BaseTheme;
            var accentColor = SettingsManager.AccentColor;
            ThemeManager.Current.ChangeTheme(window, $"{baseTheme}.{accentColor}");
        }
        catch
        {
            try { ThemeManager.Current.ChangeTheme(window, "Light.Blue"); }
            catch
            {
                // ignored
            }
        }
    }

    private sealed class NullAudioService : IAudioService
    {
        public void PlayClickSound()
        {
        }

        public void Dispose()
        {
        }
    }
}