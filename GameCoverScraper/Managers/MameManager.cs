using System.IO;
using GameCoverScraper.models;
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
            const string contextMessage = "The file 'mame.dat' could not be found in the application folder.";
            AppLogger.Log(contextMessage); // Log the event
            _ = BugReport.LogErrorAsync(new MameDatNotFoundException(contextMessage), contextMessage); // Report as error
            throw new MameDatNotFoundException($"The required data file 'mame.dat' was not found at: {datPath}");
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
            AppLogger.Log(contextMessage);
            _ = BugReport.LogErrorAsync(ex, contextMessage);
            throw new MameDatCorruptError(contextMessage, ex);
        }
        catch (IOException ex)
        {
            // Specific handling for file access errors
            const string contextMessage = "Unable to access the mame.dat file (may be in use by another process).";
            AppLogger.Log(contextMessage);
            _ = BugReport.LogErrorAsync(ex, contextMessage);
            throw new IOException(contextMessage, ex); // Re-throw IOException
        }
        catch (Exception ex)
        {
            // General exception handling
            const string contextMessage = "An unexpected error occurred while loading the mame.dat file.";
            AppLogger.Log(contextMessage);
            _ = BugReport.LogErrorAsync(ex, contextMessage);
            throw new Exception(contextMessage, ex); // Re-throw general Exception
        }
    }
}
