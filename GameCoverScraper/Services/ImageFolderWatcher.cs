using System.Collections.Concurrent;
using System.IO;
using ImageMagick;

namespace GameCoverScraper.Services;

public sealed class ImageFolderWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _recentlyProcessed = new();
    private bool _disposed;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tiff", ".tif"
    };

    public event Action<string>? ImageFound;

    public void Start(string folderPath)
    {
        Stop();

        if (!Directory.Exists(folderPath))
        {
            AppLogger.Log($"ImageFolderWatcher: folder does not exist: {folderPath}");
            return;
        }

        _watcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = "*.*",
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Changed += OnFileChanged;

        AppLogger.Log($"ImageFolderWatcher: started watching '{folderPath}'");
    }

    public void Stop()
    {
        if (_watcher == null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileCreated;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Changed -= OnFileChanged;
        _watcher.Dispose();
        _watcher = null;

        AppLogger.Log("ImageFolderWatcher: stopped");
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            await ProcessFileAsync(e.FullPath, "Created");
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, $"ImageFolderWatcher: unhandled error in OnFileCreated for '{e.FullPath}'");
        }
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            await ProcessFileAsync(e.FullPath, "Renamed");
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, $"ImageFolderWatcher: unhandled error in OnFileRenamed for '{e.FullPath}'");
        }
    }

    private async void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        try
        {
            await ProcessFileAsync(e.FullPath, "Changed");
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, $"ImageFolderWatcher: unhandled error in OnFileChanged for '{e.FullPath}'");
        }
    }

    private async Task ProcessFileAsync(string filePath, string eventType)
    {
        try
        {
            if (_disposed) return;

            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (!SupportedExtensions.Contains(extension))
                return;

            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);

            if (string.IsNullOrEmpty(fileNameWithoutExt))
                return;

            // Deduplicate: skip if this file name was processed very recently
            var dedupeKey = fileNameWithoutExt;
            if (!_recentlyProcessed.TryAdd(dedupeKey, 1))
                return;

            // Clean up old dedupe entries after 3 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(3000);
                _recentlyProcessed.TryRemove(dedupeKey, out _);
            });

            AppLogger.Log($"ImageFolderWatcher: [{eventType}] detected '{Path.GetFileName(filePath)}'");

            await _processingLock.WaitAsync();
            try
            {
                await WaitForFileReadyAsync(filePath);

                if (!File.Exists(filePath))
                    return;

                if (extension != ".png")
                {
                    var convertedPath = await ConvertToPngAsync(filePath);
                    if (convertedPath == null)
                        return;
                    filePath = convertedPath;
                }

                ImageFound?.Invoke(fileNameWithoutExt);
            }
            catch (Exception ex)
            {
                _ = ErrorLogger.LogAsync(ex, $"Error processing new image in folder: {filePath}");
                AppLogger.Log($"ImageFolderWatcher: error processing '{Path.GetFileName(filePath)}': {ex.Message}");
            }
            finally
            {
                _processingLock.Release();
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error processing new image in folder.");
            AppLogger.Log($"ImageFolderWatcher: outer error: {ex.Message}");
        }
    }

    private static async Task WaitForFileReadyAsync(string filePath)
    {
        const int maxWaitMs = 5000;
        const int pollIntervalMs = 200;
        var elapsed = 0;

        while (elapsed < maxWaitMs)
        {
            try
            {
                await using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                if (stream.Length > 0)
                    return;
            }
            catch (IOException)
            {
                // File still being written or locked
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            await Task.Delay(pollIntervalMs);
            elapsed += pollIntervalMs;
        }
    }

    private static async Task<string?> ConvertToPngAsync(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(directory ?? ".", fileNameWithoutExt + ".png");

        try
        {
            using var magickImage = new MagickImage(sourcePath);
            magickImage.AutoOrient();
            magickImage.Quality = 90;
            magickImage.Format = MagickFormat.Png;

            await magickImage.WriteAsync(targetPath);

            if (File.Exists(sourcePath) && !string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(sourcePath); }
                catch
                {
                    // Source cleanup failed, not critical
                }
            }

            return targetPath;
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, $"Failed to convert image to PNG: {sourcePath}");
            AppLogger.Log($"ImageFolderWatcher: ConvertToPngAsync failed for '{Path.GetFileName(sourcePath)}': {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        Stop();
        _processingLock.Dispose();
    }
}
