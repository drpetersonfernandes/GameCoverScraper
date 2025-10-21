using System.Web;
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

        var bingUrl = $"https://www.bing.com/images/search?q={HttpUtility.UrlEncode(searchQuery)}";
        AppLogger.Log($"Navigating WebView2 to Bing Images: {bingUrl}");

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

        // Google Images search URL
        var googleUrl = $"https://www.google.com/search?tbm=isch&q={HttpUtility.UrlEncode(searchQuery)}";
        AppLogger.Log($"Navigating WebView2 to Google Images: {googleUrl}");

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