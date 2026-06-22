using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GameCoverScraper.ApiProvider;
using GameCoverScraper.Models;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class MainWindow
{
    private async Task HandleBingWebSearchAsync(string searchQuery)
    {
        try
        {
            if (!await EnsureWebViewReadyAsync(BingWebView))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    IsSearching = false;
                    StatusMessage.Text = "Web view component is not ready.";
                });
                return;
            }

            var bingUrl = WebSearchService.BuildBingSearchUrl(searchQuery);

            await Dispatcher.InvokeAsync(() =>
            {
                LblBingWebQuery.Content = new TextBlock
                {
                    Inlines =
                    {
                        new Run("Web search for: ") { FontWeight = FontWeights.Normal },
                        new Run(searchQuery) { FontWeight = FontWeights.Bold },
                        new Run(" (Bing)")
                    }
                };
                StatusMessage.Text = "Loading Bing web search...";
                BingWebView.CoreWebView2.Navigate(bingUrl);
            });
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in HandleBingWebSearchAsync");
            await Dispatcher.InvokeAsync(() =>
            {
                IsSearching = false;
                StatusMessage.Text = "Error loading Bing search.";
            });
        }
    }

    private async Task HandleGoogleWebSearchAsync(string searchQuery)
    {
        try
        {
            if (!await EnsureWebViewReadyAsync(GoogleWebView))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    IsSearching = false;
                    StatusMessage.Text = "Web view component is not ready.";
                });
                return;
            }

            var googleUrl = WebSearchService.BuildGoogleSearchUrl(searchQuery);

            await Dispatcher.InvokeAsync(() =>
            {
                LblGoogleWebQuery.Content = new TextBlock
                {
                    Inlines =
                    {
                        new Run("Web search for: ") { FontWeight = FontWeights.Normal },
                        new Run(searchQuery) { FontWeight = FontWeights.Bold },
                        new Run(" (Google)")
                    }
                };
                StatusMessage.Text = "Loading Google web search...";
                GoogleWebView.CoreWebView2.Navigate(googleUrl);
            });
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in HandleGoogleWebSearchAsync");
            await Dispatcher.InvokeAsync(() =>
            {
                IsSearching = false;
                StatusMessage.Text = "Error loading Google search.";
            });
        }
    }

    private async Task HandleApiSearchAsync(string searchQuery, CancellationToken token)
    {
        try
        {
            var apiSearchQuery = $"\"{searchQuery.Replace("\"", "")}\"";

            List<ImageData> coverImageUrls;
            try
            {
                coverImageUrls = await FetchImagesWithRetryAsync(apiSearchQuery, token);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("API Key is not set"))
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    MessageBox.Show("Please configure your API keys in Settings > API Settings.", "Missing API Key", MessageBoxButton.OK, MessageBoxImage.Warning);
                });
                coverImageUrls = [];
            }
            catch (OperationCanceledException)
            {
                await Dispatcher.InvokeAsync(() => { StatusMessage.Text = "Search canceled."; });
                return;
            }

            if (token.IsCancellationRequested) return;

            var thumbnailSize = Settings.ThumbnailSize;

            await Dispatcher.InvokeAsync(() =>
            {
                PanelImages.Clear();

                if (coverImageUrls.Count > 0)
                {
                    foreach (var result in coverImageUrls)
                    {
                        result.ThumbnailWidth = thumbnailSize;
                        result.ThumbnailHeight = thumbnailSize;
                        PanelImages.Add(result);
                    }
                }

                IsSearching = false;
                HasSearchedApi = true;
                StatusMessage.Text = coverImageUrls.Count > 0
                    ? $"Found {coverImageUrls.Count} images."
                    : "No images found.";
                StatusImageCount.Text = PanelImages.Count.ToString(CultureInfo.InvariantCulture);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error in HandleApiSearchAsync");
            await Dispatcher.InvokeAsync(() =>
            {
                IsSearching = false;
                StatusMessage.Text = "Error during API search.";
            });
        }
    }

    private Task<List<ImageData>> FetchImagesWithRetryAsync(string searchQuery, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(Settings.GoogleKey))
            throw new InvalidOperationException("API Key is not set.");

        var google = new Google();
        return google.FetchImagesFromGoogleAsync(searchQuery, Settings, token);
    }

    private async void SaveApiImage_ClickAsync(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: ImageData { ImagePath: not null } imageData }) return;

            var imageFolderPath = GetValidatedImageFolderPath(false);
            if (string.IsNullOrEmpty(_selectedRomFileName) || string.IsNullOrEmpty(imageFolderPath)) return;

            try
            {
                var newFileName = Path.Combine(imageFolderPath, _selectedRomFileName + ".png");
                var result = await App.ImageSaveService.DownloadAndSaveImageAsync(imageData.ImagePath, newFileName);

                if (result)
                {
                    App.AudioService.PlayClickSound();
                    RemoveSelectedItem();
                    PanelImages.Clear();
                    UpdateMissingCount();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _ = ErrorLogger.LogAsync(ex, "Error saving API image");
            }
        }
        catch (Exception ex)
        {
            _ = ErrorLogger.LogAsync(ex, "Error saving API image");
        }
    }
}
