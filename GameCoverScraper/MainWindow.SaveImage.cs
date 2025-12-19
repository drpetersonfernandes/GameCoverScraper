using System.IO;
using System.Windows;
using GameCoverScraper.models;
using GameCoverScraper.Services;
using ImageMagick;

namespace GameCoverScraper;

public partial class MainWindow
{
    private async void SaveImage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: ImageData imageData } ||
                string.IsNullOrEmpty(imageData.ImagePath) ||
                imageData.ImagePath.StartsWith("pack://", StringComparison.Ordinal))
            {
                return;
            }

            AppLogger.Log($"Save image clicked for image: {imageData.ImagePath}");

            if (string.IsNullOrEmpty(_imageFolderPath) || string.IsNullOrEmpty(_selectedGameFileName))
            {
                MessageBox.Show("Please select both an image folder and an item from the list.", "Warning",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusMessageText = $"Saving image for '{_selectedGameFileName}'...";
            var localPath = Path.Combine(_imageFolderPath, _selectedGameFileName + ".png");
            AppLogger.Log($"Attempting to save image for '{_selectedGameFileName}' to '{localPath}'.");

            // Check if file already exists and prompt for overwrite
            if (File.Exists(localPath))
            {
                var result = MessageBox.Show($"The file '{_selectedGameFileName}.png' already exists. Do you want to overwrite it?",
                    "File Exists", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result != MessageBoxResult.Yes)
                {
                    AppLogger.Log("User chose not to overwrite existing file.");
                    StatusMessageText = "Save canceled by user.";
                    return;
                }

                AppLogger.Log("User chose to overwrite existing file.");
            }

            try
            {
                using var response = await HttpClientHelper.Client.GetAsync(imageData.ImagePath).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                    if (await ConvertStreamToPngAndSave(stream, localPath).ConfigureAwait(false))
                    {
                        // Use Dispatcher to ensure UI operations happen on the UI thread
                        await Dispatcher.InvokeAsync(() =>
                        {
                            RemoveSelectedItem();
                            PlaySound.PlayClickSound();
                            StatusMessageText = $"Image saved: {_selectedGameFileName}.png";
                        });
                    }
                    else
                    {
                        StatusMessageText = "Failed to convert and save image.";
                    }
                }
                else
                {
                    StatusMessageText = "Failed to download image.";
                    MessageBox.Show($"Failed to download image. HTTP Status: {response.StatusCode}", "Download Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusMessageText = "Error saving image.";
                _ = BugReport.LogErrorAsync(ex, "Error saving image.");
                MessageBox.Show("There was an error saving the image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Error in SaveImage_Click outer try-catch.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Converts an image from a source stream to a PNG format at a destination path.
    /// Preserves the aspect ratio, dimensions, and transparency using SixLabors.ImageSharp.
    /// </summary>
    /// <param name="inputStream">The stream containing the source image data.</param>
    /// <param name="outputPath">The path where the PNG image will be saved.</param>
    /// <returns>True if conversion was successful, false otherwise.</returns>
    private static async Task<bool> ConvertStreamToPngAndSave(Stream inputStream, string outputPath)
    {
        var tempOutputPath = outputPath + ".tmp" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            AppLogger.Log($"Converting stream to PNG and saving to '{outputPath}' via temp file '{tempOutputPath}'.");

            // Ensure the destination directory exists
            var destDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Magick.NET automatically handles various formats and preserves properties
            using (var image = new MagickImage(inputStream))
            {
                // Set format to PNG and save to temporary file first to avoid locking issues
                image.Format = MagickFormat.Png;
                await image.WriteAsync(tempOutputPath).ConfigureAwait(false);
            }

            // Atomically move the temporary file to the final destination, overwriting if it exists.
            File.Move(tempOutputPath, outputPath, true);
            AppLogger.Log($"Successfully saved image from stream to '{outputPath}'.");

            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("There was an error saving the image.\n\n" +
                            "The developer will try to fix this.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReport.LogErrorAsync(ex, "General error during image conversion.");

            return false;
        }
        finally
        {
            // Ensure the temporary file is cleaned up in case of an error.
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