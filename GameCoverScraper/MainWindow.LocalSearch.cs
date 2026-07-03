using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using GameCoverScraper.Models;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class MainWindow
{
    private CancellationTokenSource? _webSearchCts;

    private void LstMissingImages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        CommandManager.InvalidateRequerySuggested();

        // Cancel any pending searches
        _findSimilarCts?.Cancel();
        _findSimilarCts?.Dispose();
        _findSimilarCts = null;
        _webSearchCts?.Cancel();
        _webSearchCts?.Dispose();
        _webSearchCts = null;

        if (LstMissingImages.SelectedItem is not MissingImageItem selectedItem)
        {
            SimilarImages.Clear();
            PanelImages.Clear();
            LblLocalSearchQuery.Content = null;
            LblApiSearchQuery.Content = null;
            IsFindingSimilar = false;
            IsSearching = false;
            HasSearchedSimilar = false;
            HasSearchedApi = false;
            return;
        }

        var selectedItemRomName = selectedItem.RomName;
        var selectedItemSearchName = selectedItem.SearchName;
        _selectedRomFileName = selectedItemRomName;

        var activeTab = SearchTabControl.SelectedIndex;
        _imageFolderWatcher?.PendingRenameTarget = selectedItemRomName;

        try { Clipboard.SetText(selectedItemRomName); }
        catch (System.Runtime.InteropServices.COMException)
        {
            // Clipboard may be locked by another process
        }

        // Always do local search
        _ = RunLocalSearchAsync(selectedItemSearchName, selectedItemRomName);

        // Build the search query for web/API tabs
        var extraQuery = TxtExtraQuery.Text.Trim();
        var cleanedSearchName = SearchQueryHelper.CleanSearchQuery(selectedItemSearchName);
        var searchQuery = !string.IsNullOrWhiteSpace(extraQuery) ? $"\"{cleanedSearchName}\" {extraQuery}" : $"\"{cleanedSearchName}\"";

        // Dispatch based on active tab
        switch (activeTab)
        {
            case 1: // Google Web
                IsSearching = true;
                StatusMessage.Text = "Searching Google...";
                _ = HandleGoogleWebSearchAsync(searchQuery);
                break;
            case 2: // Bing Web
                IsSearching = true;
                StatusMessage.Text = "Searching Bing...";
                _ = HandleBingWebSearchAsync(searchQuery);
                break;
            case 3: // Google API
                IsSearching = true;
                StatusMessage.Text = "Searching Google API...";
                _webSearchCts = new CancellationTokenSource();
                LblApiSearchQuery.Content = new TextBlock
                {
                    Inlines =
                    {
                        new Run("API search for: ") { FontWeight = FontWeights.Normal },
                        new Run(searchQuery) { FontWeight = FontWeights.Bold }
                    }
                };
                _ = HandleApiSearchAsync(searchQuery, _webSearchCts.Token);
                break;
        }
    }

    private async Task RunLocalSearchAsync(string searchName, string romName)
    {
        var imageFolderPath = GetValidatedImageFolderPath();
        if (string.IsNullOrEmpty(imageFolderPath))
        {
            IsFindingSimilar = false;
            return;
        }

        _findSimilarCts = new CancellationTokenSource();
        var cancellationToken = _findSimilarCts.Token;

        IsFindingSimilar = true;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested) return;

            var textBlock = new TextBlock();
            textBlock.Inlines.Add(new Run("Search Query: "));
            textBlock.Inlines.Add(new Run($"{searchName} ") { FontWeight = FontWeights.Bold });
            textBlock.Inlines.Add(new Run("for ROM: "));
            textBlock.Inlines.Add(new Run($"{romName} ") { FontWeight = FontWeights.Bold });
            textBlock.Inlines.Add(new Run("with "));
            textBlock.Inlines.Add(new Run($"{Settings.SelectedSimilarityAlgorithm} ") { FontWeight = FontWeights.Bold });
            textBlock.Inlines.Add(new Run("algorithm"));
            LblLocalSearchQuery.Content = textBlock;

            SimilarImages.Clear();
        });

        try
        {
            await _findSimilarSemaphore.WaitAsync(cancellationToken);
            try
            {
                SimilarityCalculationResult similarityResult;
                try
                {
                    similarityResult = await ButtonFactory.CreateSimilarImagesCollectionAsync(
                        searchName,
                        imageFolderPath,
                        Settings.SimilarityThreshold,
                        Settings.SelectedSimilarityAlgorithm,
                        cancellationToken,
                        imageData =>
                        {
                            _ = Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (!cancellationToken.IsCancellationRequested)
                                {
                                    SimilarImages.Add(imageData);
                                    HasSearchedSimilar = true;
                                }
                            });
                        }
                    );
                }
                finally
                {
                    if (!cancellationToken.IsCancellationRequested)
                        await Application.Current.Dispatcher.InvokeAsync(static () => { });
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    HasSearchedSimilar = true;

                    if (similarityResult.ProcessingErrors.Count > 0)
                    {
                        var errorSummary = $"Encountered {similarityResult.ProcessingErrors.Count} issues while processing images:\n\n";
                        errorSummary += string.Join("\n", similarityResult.ProcessingErrors.Take(5));
                        if (similarityResult.ProcessingErrors.Count > 5)
                        {
                            errorSummary += $"\n...and {similarityResult.ProcessingErrors.Count - 5} more.";
                        }

                        MessageBox.Show(errorSummary, "Image Processing Warnings", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }

                    LocalImageScrollViewer.ScrollToTop();
                }
            }
            finally { _findSimilarSemaphore.Release(); }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                MessageBox.Show($"Error searching for similar images: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _ = ErrorLogger.LogAsync(ex, "Error in RunLocalSearchAsync");
            }
        }
        finally { IsFindingSimilar = false; }
    }

    private void ImageCell_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ImageData { ImagePath: not null } imageData })
        {
            _ = UseImageAsync(imageData.ImagePath);
        }
    }

    public async Task UseImageAsync(string? imagePath)
    {
        var imageFolderPath = GetValidatedImageFolderPath(false);
        if (string.IsNullOrEmpty(_selectedRomFileName) || string.IsNullOrEmpty(imagePath) || string.IsNullOrEmpty(imageFolderPath)) return;

        var safeFileName = SearchQueryHelper.SanitizeFileName(_selectedRomFileName);
        var newFileName = Path.Combine(imageFolderPath, safeFileName + ".png");
        _imageFolderWatcher?.PreRegisterExpectedFile(newFileName);

        try
        {
            var result = await ImageProcessor.ConvertAndSaveImageAsync(imagePath, newFileName, CancellationToken.None);
            if (result.Success)
            {
                App.AudioService.PlayClickSound();
                RemoveSelectedItem();
                SimilarImages.Clear();
                UpdateMissingCount();
            }
            else
            {
                MessageBox.Show("Failed to save the image.", "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Unexpected error saving image: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = ErrorLogger.LogAsync(ex, $"Unexpected error in UseImage: {imagePath}");
        }
    }

    private void Image_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ImageData imageData } element) return;

        if (imageData.ImagePath != null)
        {
            element.ContextMenu = ButtonFactory.CreateContextMenu(imageData.ImagePath, path => { _ = UseImageAsync(path); }, imageData.CachedContextMenu);
            imageData.CachedContextMenu = element.ContextMenu;
        }
    }
}
