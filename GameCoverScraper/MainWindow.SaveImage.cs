using System.IO;
using System.Windows;
using GameCoverScraper.models;
using GameCoverScraper.Services;

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
                var success = await App.ImageSaveService.DownloadAndSaveImageAsync(imageData.ImagePath, localPath).ConfigureAwait(false);

                if (success)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        RemoveSelectedItem();
                        PlaySound.PlayClickSound();
                        StatusMessageText = $"Image saved: {_selectedGameFileName}.png";
                    });
                }
                else
                {
                    StatusMessageText = "Failed to download or save image.";
                }
            }
            catch (Exception ex)
            {
                StatusMessageText = "Error saving image.";
                _ = BugReport.LogErrorAsync(ex, "Error saving image.");
                MessageBox.Show("There was an error saving the image.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Error in SaveImage_Click outer try-catch.").ConfigureAwait(false);
        }
    }
}
