using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using GameCoverScraper.models;

namespace GameCoverScraper.Services;

public static class AppLogger
{
    private static readonly string LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.log");
    private static readonly Lock Lock = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ObservableCollection<LogEntry> LogMessages { get; } = new();

    static AppLogger()
    {
        // Clear log file on startup
        if (!File.Exists(LogFilePath)) return;

        try
        {
            File.WriteAllText(LogFilePath, string.Empty);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to clear log file: {ex.Message}");
        }
    }

    public static void Log(string message, [CallerMemberName] string memberName = "",
        [CallerFilePath] string sourceFilePath = "", [CallerLineNumber] int sourceLineNumber = 0)
    {
        var logEntryText =
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Path.GetFileNameWithoutExtension(sourceFilePath)}.{memberName}:{sourceLineNumber}] - {message}";
        var logEntry = new LogEntry { Message = logEntryText };

        // Dispatch to UI thread for updating ObservableCollection
        Application.Current?.Dispatcher.Invoke(() =>
        {
            LogMessages.Add(logEntry);
            // Optional: Limit the number of messages in memory
            while (LogMessages.Count > 5000)
            {
                LogMessages.RemoveAt(0);
            }
        });

        // Write to file
        lock (Lock)
        {
            try
            {
                File.AppendAllText(LogFilePath, logEntryText + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    public static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return string.Empty;

        try
        {
            using var jDoc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(jDoc, JsonOptions);
        }
        catch (JsonException)
        {
            // Not a valid JSON, return as is
            return json;
        }
    }
}