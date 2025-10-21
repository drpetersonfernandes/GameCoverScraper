using System.Windows;
using System.Windows.Navigation; // Add this using directive
using System.Diagnostics;
using GameCoverScraper.Managers;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class ApiSettingsWindow
{
    private readonly SettingsManager _settingsManager;

    public ApiSettingsWindow(SettingsManager settingsManager)
    {
        InitializeComponent();
        _settingsManager = settingsManager;
        LoadSettings();
    }

    private void LoadSettings()
    {
        TxtGoogleKey.Text = _settingsManager.GoogleKey;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _settingsManager.GoogleKey = TxtGoogleKey.Text.Trim();

            _settingsManager.SaveSettings();
            AppLogger.Log("API settings saved successfully.");
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            _ = BugReport.LogErrorAsync(ex, "Error saving API settings.");
        }
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            // Use ShellExecute to open the URL in the default browser
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
            e.Handled = true; // Mark the event as handled
        }
        catch (Exception ex)
        {
            // Log the error and show a user-friendly message
            _ = BugReport.LogErrorAsync(ex, $"Failed to open hyperlink: {e.Uri.AbsoluteUri}");
            MessageBox.Show("Could not open the link. Please copy and paste the URL into your browser.",
                "Link Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
