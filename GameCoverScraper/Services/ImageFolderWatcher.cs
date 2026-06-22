using System.IO;
using ImageMagick;

namespace GameCoverScraper.Services;

public sealed class ImageFolderWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private readonly SemaphoreSlim _processingLock = new(1, 1);
    private bool _disposed;

    public event Action<string>? ImageFound;

    public void Start(string folderPath)
    {
        Stop();

        if (!Directory.Exists(folderPath))
            return;

        _watcher = new FileSystemWatcher(folderPath)
        {
            NotifyFilter = NotifyFilters.FileName,
            Filter = "*.*",
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
    }

    public void Stop()
    {
        if (_watcher == null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileCreated;
        _watcher.Dispose();
        _watcher = null;
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_disposed) return;

            var filePath = e.FullPath;
            var extension = Path.GetExtension(filePath).ToLowerInvariant();

            if (extension is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".webp" or ".tiff" or ".tif"))
                return;

            await _processingLock.WaitAsync();
            try
            {
                await WaitForFileReadyAsync(filePath);

                if (!File.Exists(filePath))
                    return;

                var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);

                if (extension != ".png")
                {
                    filePath = await ConvertToPngAsync(filePath);
                    if (filePath == null)
                        return;
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
