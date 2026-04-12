using System.IO;
using ImageMagick;

namespace GameCoverScraper.Services;

public class ImageSaveService
{
    /// <summary>
    /// Downloads an image from the given URL and saves it as a PNG file at the specified path.
    /// </summary>
    /// <param name="imageUrl">The URL of the image to download.</param>
    /// <param name="outputPath">The path where the PNG image will be saved.</param>
    /// <returns>True if the download and save was successful, false otherwise.</returns>
    public async Task<bool> DownloadAndSaveImageAsync(string imageUrl, string outputPath)
    {
        try
        {
            using var response = await HttpClientHelper.Client.GetAsync(imageUrl).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                AppLogger.Log($"Failed to download image. HTTP Status: {response.StatusCode}");
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            return await ConvertStreamToPngAndSave(stream, outputPath).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error downloading and saving image from '{imageUrl}': {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Error downloading and saving image.");
            return false;
        }
    }

    /// <summary>
    /// Converts an image from a source stream to a PNG format at a destination path.
    /// Preserves the aspect ratio, dimensions, and transparency using Magick.NET.
    /// </summary>
    /// <param name="inputStream">The stream containing the source image data.</param>
    /// <param name="outputPath">The path where the PNG image will be saved.</param>
    /// <returns>True if conversion was successful, false otherwise.</returns>
    public async Task<bool> ConvertStreamToPngAndSave(Stream inputStream, string outputPath)
    {
        var tempOutputPath = outputPath + ".tmp" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            AppLogger.Log($"Converting stream to PNG and saving to '{outputPath}' via temp file '{tempOutputPath}'.");

            var destDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            using (var image = new MagickImage(inputStream))
            {
                image.Format = MagickFormat.Png;
                await image.WriteAsync(tempOutputPath).ConfigureAwait(false);
            }

            File.Move(tempOutputPath, outputPath, true);
            AppLogger.Log($"Successfully saved image from stream to '{outputPath}'.");

            return true;
        }
        catch (Exception ex)
        {
            // Log the error - UI notifications should be handled by the caller
            AppLogger.Log($"Error saving image: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "General error during image conversion.");

            return false;
        }
        finally
        {
            if (File.Exists(tempOutputPath))
            {
                try
                {
                    File.Delete(tempOutputPath);
                }
                catch (Exception cleanupEx)
                {
                    AppLogger.Log($"Failed to clean up temporary file '{tempOutputPath}': {cleanupEx.Message}");
                    _ = BugReport.LogErrorAsync(cleanupEx, $"Failed to clean up temp file: {tempOutputPath}");
                }
            }
        }
    }
}
