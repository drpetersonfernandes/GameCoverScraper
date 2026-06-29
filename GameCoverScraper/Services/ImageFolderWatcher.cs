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

    private string? _pendingRenameTarget;
    private readonly object _renameLock = new();

    public string? PendingRenameTarget
    {
        get { lock (_renameLock) { return _pendingRenameTarget; } }
        set
        {
            lock (_renameLock)
            {
                var old = _pendingRenameTarget;
                _pendingRenameTarget = value;
                if (old != value)
                    AppLogger.Log($"ImageFolderWatcher: PendingRenameTarget changed from '{old}' to '{value}'");
            }
        }
    }

    public bool TryClearPendingRenameTarget(string? expectedValue)
    {
        lock (_renameLock)
        {
            if (_pendingRenameTarget != expectedValue)
                return false;

            var old = _pendingRenameTarget;
            _pendingRenameTarget = null;
            AppLogger.Log($"ImageFolderWatcher: PendingRenameTarget cleared from '{old}'");
            return true;
        }
    }

    public void PreRegisterExpectedFile(string filePath)
    {
        _recentlyProcessed.TryAdd(filePath, 1);
        _ = Task.Run(async () =>
        {
            await Task.Delay(5000);
            _recentlyProcessed.TryRemove(filePath, out _);
        });
        AppLogger.Log($"ImageFolderWatcher: pre-registered '{Path.GetFileName(filePath)}' so watcher will skip it");
    }

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
            InternalBufferSize = 64 * 1024,
            EnableRaisingEvents = true
        };

        _watcher.Created += OnFileCreated;
        _watcher.Renamed += OnFileRenamed;
        _watcher.Error += OnWatcherError;

        AppLogger.Log($"ImageFolderWatcher: started watching '{folderPath}'");
    }

    public void Stop()
    {
        if (_watcher == null) return;

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFileCreated;
        _watcher.Renamed -= OnFileRenamed;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _watcher = null;

        // Wait for any in-flight ProcessFileAsync to complete
        if (!_processingLock.Wait(TimeSpan.FromSeconds(15)))
        {
            AppLogger.Log("ImageFolderWatcher: timed out waiting for in-flight processing to complete");
        }
        else
        {
            _processingLock.Release();
        }

        AppLogger.Log("ImageFolderWatcher: stopped");
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _ = ErrorLogger.LogAsync(e.GetException(), "ImageFolderWatcher: FileSystemWatcher error (buffer overflow or system error)");
    }

    private async void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            if (_disposed) return;

            AppLogger.Log($"ImageFolderWatcher: OnFileCreated fired for '{e.FullPath}'");

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

            AppLogger.Log($"ImageFolderWatcher: OnFileRenamed fired for '{e.FullPath}'");

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
            {
                AppLogger.Log($"ImageFolderWatcher: skipping '{Path.GetFileName(filePath)}' — extension '{extension}' not supported");
                return;
            }

            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);

            if (string.IsNullOrEmpty(fileNameWithoutExt))
                return;

            // Deduplicate: skip if this exact file path was processed very recently
            if (!_recentlyProcessed.TryAdd(filePath, 1))
            {
                AppLogger.Log($"ImageFolderWatcher: skipping '{Path.GetFileName(filePath)}' — dedup hit");
                return;
            }

            // Clean up old dedupe entries after 5 seconds
            var dedupeKey = filePath;
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                _recentlyProcessed.TryRemove(dedupeKey, out _);
            });

            await _processingLock.WaitAsync();
            try
            {
                await WaitForFileReadyAsync(filePath);

                if (!File.Exists(filePath))
                {
                    AppLogger.Log($"ImageFolderWatcher: file disappeared after wait: '{filePath}'");
                    return;
                }

                var renameTarget = PendingRenameTarget;
                AppLogger.Log($"ImageFolderWatcher: processing '{Path.GetFileName(filePath)}' — PendingRenameTarget = '{renameTarget}'");

                if (!string.IsNullOrEmpty(renameTarget))
                {
                    var directory = Path.GetDirectoryName(filePath);
                    var renamedPath = Path.Combine(directory ?? ".", renameTarget + extension);

                    if (File.Exists(renamedPath))
                    {
                        AppLogger.Log($"ImageFolderWatcher: target '{Path.GetFileName(renamedPath)}' already exists, deleting it first");
                        try { File.Delete(renamedPath); }
                        catch (Exception ex)
                        {
                            _ = ErrorLogger.LogAsync(ex, $"ImageFolderWatcher: failed to delete existing target '{renamedPath}'");
                        }
                    }

                    var renamed = await MoveFileWithRetryAsync(filePath, renamedPath);
                    if (renamed)
                    {
                        TryClearPendingRenameTarget(renameTarget);
                        _recentlyProcessed.TryAdd(renamedPath, 1);
                        AppLogger.Log($"ImageFolderWatcher: renamed '{Path.GetFileName(filePath)}' to '{Path.GetFileName(renamedPath)}'");
                        filePath = renamedPath;
                        fileNameWithoutExt = renameTarget;
                    }
                    else
                    {
                        AppLogger.Log($"ImageFolderWatcher: failed to rename '{filePath}' to '{renamedPath}' after retries — PendingRenameTarget kept for next file");
                    }
                }

                if (!Path.GetExtension(filePath).Equals(".png", StringComparison.OrdinalIgnoreCase))
                {
                    var convertedPath = await ConvertToPngWithRetryAsync(filePath);
                    if (convertedPath == null)
                    {
                        AppLogger.Log($"ImageFolderWatcher: conversion to PNG failed for '{filePath}' — ImageFound NOT fired");
                        return;
                    }

                    _recentlyProcessed.TryAdd(convertedPath, 1);

                    filePath = convertedPath;
                }

                fileNameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
                AppLogger.Log($"ImageFolderWatcher: firing ImageFound for '{fileNameWithoutExt}'");
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
