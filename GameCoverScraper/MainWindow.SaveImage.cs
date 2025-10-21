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
                var result = MessageBox.Show(
                    $"The file '{_selectedGameFileName}.png' already exists. Do you want to overwrite it?",
                    "File Exists",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
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

                    if (ConvertToPngAndSave(stream, localPath))
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
                    MessageBox.Show($"Failed to download image. HTTP Status: {response.StatusCode}", "Download Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusMessageText = "Error saving image.";
                MessageBox.Show("There was an error saving the image: " + ex.Message, "Warning", MessageBoxButton.OK,
                    MessageBoxImage.Error);
                _ = BugReport.LogErrorAsync(ex, "Error saving image.").ConfigureAwait(false);
                return;
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Error in SaveImage_Click outer try-catch.").ConfigureAwait(false);
        }
    }

    private static bool ConvertToPngAndSave(Stream inputStream, string outputPath)
    {
        try
        {
            AppLogger.Log($"Converting stream to PNG and saving to '{outputPath}'.");
            using var image = new MagickImage(inputStream);
            image.Format = MagickFormat.Png;
            image.Write(outputPath);
            AppLogger.Log($"Successfully saved image to '{outputPath}'.");

            return true;
        }
        catch (MagickException ex)
        {
            MessageBox.Show("Magick.NET error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReport.LogErrorAsync(ex, "Magick.NET error during image conversion.").ConfigureAwait(false);

            return false;
        }
        catch (Exception ex)
        {
            MessageBox.Show("General error: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReport.LogErrorAsync(ex, "General error during image conversion.").ConfigureAwait(false);

            return false;
        }
    }
}