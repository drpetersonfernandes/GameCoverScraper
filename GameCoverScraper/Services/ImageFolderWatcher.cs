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
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tiff", ".tif",
        ".avif", ".heic", ".heif", ".ico", ".svg", ".jxl", ".jp2"
    };

    public event Action<string>? ImageFound;

    public string? PendingRenameTarget { get; set; }

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
            NotifyFilter = NotifyFilters.FileName,
            Filter = "*.*",
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;

        AppLogger.Log($"ImageFolderWatcher: started watching '{folderPath}'");
    }

    public void Stop()
    {
        if (_watcher == null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileCreated;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Dispose();
        _watcher = null;

        AppLogger.Log("ImageFolderWatcher: stopped");
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_disposed) return;

            try
            {
                await ProcessFileAsync(e.FullPath);
            }
            catch (Exception ex)
            {
                _ = ErrorLogger.LogAsync(ex, $"ImageFolderWatcher: unhandled error in OnFileCreated for '{e.FullPath}'");
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in method OnFileCreated");
        }
    }

    private async void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        try
        {
            if (_disposed) return;

            try
            {
                await ProcessFileAsync(e.FullPath);
            }
            catch (Exception ex)
            {
                _ = ErrorLogger.LogAsync(ex, $"ImageFolderWatcher: unhandled error in OnFileRenamed for '{e.FullPath}'");
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in method OnFileRenamed");
        }
    }

    private async Task ProcessFileAsync(string filePath)
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
            if (!_recentlyProcessed.TryAdd(fileNameWithoutExt, 1))
                return;

            // Clean up old dedupe entries after 5 seconds
            var ext = fileNameWithoutExt;
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                _recentlyProcessed.TryRemove(ext, out _);
            });

            await _processingLock.WaitAsync();
            try
            {
                await WaitForFileReadyAsync(filePath);

                if (!File.Exists(filePath))
                    return;

                var renameTarget = PendingRenameTarget;
                if (!string.IsNullOrEmpty(renameTarget))
                {
                    PendingRenameTarget = null;
                    var directory = Path.GetDirectoryName(filePath);
                    var renamedPath = Path.Combine(directory ?? ".", renameTarget + extension);

                    var renamed = await MoveFileWithRetryAsync(filePath, renamedPath);
                    if (renamed)
                    {
                        _recentlyProcessed.TryAdd(renameTarget, 1);
                        filePath = renamedPath;
                        fileNameWithoutExt = renameTarget;
                        AppLogger.Log($"ImageFolderWatcher: renamed '{Path.GetFileName(filePath)}' to '{Path.GetFileName(renamedPath)}'");
                    }
                    else
                    {
                        AppLogger.Log($"ImageFolderWatcher: failed to rename '{filePath}' to '{renamedPath}' after retries");
                    }
                }

                if (!Path.GetExtension(filePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var convertedPath = await ConvertToPngWithRetryAsync(filePath);
                    if (convertedPath == null)
                        return;

                    _recentlyProcessed.TryAdd(fileNameWithoutExt, 1);

                    filePath = convertedPath;
                }

                ImageFound?.Invoke(fileNameWithoutExt);
            }
            catch (Exception ex)
            {
                _ = ErrorLogger.LogAsync(ex, $"Error processing new image in folder: {filePath}");
            }
            finally
            {
                _processingLock.Release();
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error processing new image in folder.");
        }
    }

    private static async Task WaitForFileReadyAsync(string filePath)
    {
        const int maxWaitMs = 10000;
        const int pollIntervalMs = 250;
        const int stableChecksRequired = 2;
        var elapsed = 0;
        long lastSize = -1;
        var stableCount = 0;

        while (elapsed < maxWaitMs)
        {
            try
            {
                await using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 0)
                {
                    if (stream.Length == lastSize)
                    {
                        stableCount++;
                        if (stableCount >= stableChecksRequired)
                            return;
                    }
                    else
                    {
                        stableCount = 0;
                        lastSize = stream.Length;
                    }
                }
                else
                {
                    stableCount = 0;
                    lastSize = 0;
                }
            }
            catch (IOException)
            {
                stableCount = 0;
                lastSize = -1;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }

            await Task.Delay(pollIntervalMs);
            elapsed += pollIntervalMs;
        }
    }

    private static async Task<bool> MoveFileWithRetryAsync(string sourcePath, string targetPath)
    {
        const int maxRetries = 5;
        const int baseDelayMs = 200;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                File.Move(sourcePath, targetPath);
                return true;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                var delay = baseDelayMs * Math.Pow(2, attempt - 1);
                await Task.Delay((int)delay);
            }
            catch (UnauthorizedAccessException) when (attempt < maxRetries)
            {
                var delay = baseDelayMs * Math.Pow(2, attempt - 1);
                await Task.Delay((int)delay);
            }
            catch (Exception ex)
            {
                _ = ErrorLogger.LogAsync(ex, $"MoveFileWithRetryAsync: attempt {attempt} failed for '{sourcePath}' -> '{targetPath}'");
                if (attempt >= maxRetries) return false;

                var delay = baseDelayMs * Math.Pow(2, attempt - 1);
                await Task.Delay((int)delay);
            }
        }

        return false;
    }

    private static async Task<string?> ConvertToPngWithRetryAsync(string sourcePath)
    {
        const int maxRetries = 3;
        const int baseDelayMs = 300;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            var result = await ConvertToPngAsync(sourcePath);
            if (result != null)
                return result;

            if (attempt < maxRetries)
            {
                AppLogger.Log($"ImageFolderWatcher: convert retry {attempt}/{maxRetries} for '{sourcePath}'");
                var delay = baseDelayMs * Math.Pow(2, attempt - 1);
                await Task.Delay((int)delay);
            }
        }

        return null;
    }

    private static async Task<string?> ConvertToPngAsync(string sourcePath)
    {
        var directory = Path.GetDirectoryName(sourcePath);
        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(sourcePath);
        var targetPath = Path.Combine(directory ?? ".", fileNameWithoutExt + ".png");

        try
        {
            var settings = GetMagickReadSettings(sourcePath);
            using var magickImage = new MagickImage(sourcePath, settings);
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
            return null;
        }
    }

    private static MagickReadSettings GetMagickReadSettings(string filePath)
    {
        var settings = new MagickReadSettings();
        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        settings.Format = ext switch
        {
            ".avif" => MagickFormat.Avif,
            ".heic" => MagickFormat.Heic,
            ".heif" => MagickFormat.Heif,
            ".jxl" => MagickFormat.Jxl,
            ".jp2" => MagickFormat.Jp2,
            _ => MagickFormat.Unknown
        };

        return settings;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        Stop();
        _processingLock.Dispose();
    }
}
