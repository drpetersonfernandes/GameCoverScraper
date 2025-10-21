using System.Windows;
using System.Diagnostics;
using System.Windows.Navigation;
using System.Reflection;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class AboutWindow
{
    public AboutWindow()
    {
        InitializeComponent();
        DataContext = this;
        AppVersionTextBlock.Text = ApplicationVersion;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show("Unable to open the link.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReport.LogErrorAsync(ex, "Error in Hyperlink_RequestNavigate");
        }
    }

    private static string ApplicationVersion
    {
        get
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return "Version: " + (version?.ToString() ?? "Unknown");
        }
    }
}