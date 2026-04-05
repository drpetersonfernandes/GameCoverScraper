using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class MainWindow
{
    private async Task HandleBingWebSearch(string searchQuery)
    {
        if (WebView?.CoreWebView2 == null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                IsSearching = false;
                StatusMessageText = "Web view component is not ready.";
                MessageBox.Show("The web search component is not ready. Please try again or restart the application.", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            return;
        }

        var bingUrl = WebSearchService.BuildBingSearchUrl(searchQuery);

        await Dispatcher.InvokeAsync(() =>
        {
            LblSearchQuery.Content = new TextBlock
            {
                Inlines =
                {
                    new Run("Web search for: ") { FontWeight = FontWeights.Normal },
                    new Run(searchQuery) { FontWeight = FontWeights.Bold },
                    new Run(" (Bing)")
                }
            };
            StatusMessageText = "Loading Bing web search...";
            WebView.CoreWebView2.Navigate(bingUrl);
        });
    }

    private async Task HandleGoogleWebSearch(string searchQuery)
    {
        if (WebView?.CoreWebView2 == null)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                IsSearching = false;
                StatusMessageText = "Web view component is not ready.";
                MessageBox.Show("The web search component is not ready. Please try again or restart the application.", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            });
            return;
        }

        var googleUrl = WebSearchService.BuildGoogleSearchUrl(searchQuery);

        await Dispatcher.InvokeAsync(() =>
        {
            LblSearchQuery.Content = new TextBlock
            {
                Inlines =
                {
                    new Run("Web search for: ") { FontWeight = FontWeights.Normal },
                    new Run(searchQuery) { FontWeight = FontWeights.Bold },
                    new Run(" (Google)")
                }
            };
            StatusMessageText = "Loading Google web search...";
            WebView.CoreWebView2.Navigate(googleUrl);
        });
    }
}
