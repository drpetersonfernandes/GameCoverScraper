using System.IO;
using System.Windows;
using GameCoverScraper.Services;
using MessagePack;

namespace GameCoverScraper.Managers;

[MessagePackObject]
public class MameManager
{
    [Key(0)]
    public string MachineName { get; set; } = string.Empty;

    [Key(1)]
    public string Description { get; set; } = string.Empty;

    private static readonly string DefaultDatPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mame.dat");

    public static List<MameManager> LoadFromDat()
    {
        var datPath = DefaultDatPath;

        if (!File.Exists(datPath))
        {
            // Notify developer
            const string contextMessage = "The file 'mame.dat' could not be found in the application folder.";
            _ = BugReport.LogErrorAsync(null, contextMessage);

            // Notify user with a friendly message
            ShowUserNotification("Missing Data File",
                "The required data file 'mame.dat' was not found.\n\n" +
                "Please ensure the file is placed in the application directory:\n" +
                $"{AppDomain.CurrentDomain.BaseDirectory}\n\n" +
                "The application will continue with an empty game list.");

            return new List<MameManager>(); // return an empty list
        }

        try
        {
            // Read the binary data from the DAT file
            var binaryData = File.ReadAllBytes(datPath);

            // Deserialize the binary data to a list of MameManager objects
            return MessagePackSerializer.Deserialize<List<MameManager>>(binaryData);
        }
        catch (MessagePackSerializationException ex)
        {
            // Specific handling for serialization errors
            const string contextMessage = "The mame.dat file is corrupted or in an invalid format.";
            _ = BugReport.LogErrorAsync(ex, contextMessage);

            ShowUserNotification("Corrupted Data File",
                "The data file 'mame.dat' appears to be corrupted or in an invalid format.\n\n" +
                "Please verify the file integrity or obtain a fresh copy.\n\n" +
                "The application will continue with an empty game list.");

            return new List<MameManager>();
        }
        catch (IOException ex)
        {
            // Specific handling for file access errors
            const string contextMessage = "Unable to access the mame.dat file (may be in use by another process).";
            _ = BugReport.LogErrorAsync(ex, contextMessage);

            ShowUserNotification("File Access Error",
                "Unable to access the 'mame.dat' file.\n\n" +
                "Please ensure the file is not being used by another application and try again.\n\n" +
                "The application will continue with an empty game list.");

            return new List<MameManager>();
        }
        catch (Exception ex)
        {
            // General exception handling
            const string contextMessage = "An unexpected error occurred while loading the mame.dat file.";
            _ = BugReport.LogErrorAsync(ex, contextMessage);

            ShowUserNotification("Unexpected Error",
                "An unexpected error occurred while loading the game data.\n\n" +
                "Please try restarting the application. If the problem persists, " +
                "contact support with the error details.\n\n" +
                "The application will continue with an empty game list.");

            return new List<MameManager>();
        }
    }

    private static void ShowUserNotification(string title, string message)
    {
        try
        {
            // Use MessageBox for user notification
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch
        {
            // Fallback to console if GUI is not available
            Console.WriteLine($"[WARNING] {title}: {message}");
        }
    }

    // Optional: Add method to validate if the loaded data is meaningful
    public static bool IsDataValid(List<MameManager> data)
    {
        return data != null && data.Count > 0 &&
               data.Any(x => !string.IsNullOrEmpty(x.MachineName));
    }
}
