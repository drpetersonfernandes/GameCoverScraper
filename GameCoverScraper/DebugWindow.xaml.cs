using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;
using System.Windows;
using GameCoverScraper.models;
using GameCoverScraper.Services;

namespace GameCoverScraper;

public partial class DebugWindow
{
    public event EventHandler? WindowHidden;
    private bool _isForceClosing;

    public DebugWindow()
    {
        InitializeComponent();

        // Populate the TextBox with existing logs on startup
        var initialLogText = new StringBuilder();
        foreach (var logEntry in AppLogger.LogMessages)
        {
            initialLogText.AppendLine(logEntry.Message);
        }

        LogTextBox.Text = initialLogText.ToString();
        LogTextBox.ScrollToEnd();

        // Subscribe to collection changes to append new logs
        if (AppLogger.LogMessages is INotifyCollectionChanged incc)
        {
            incc.CollectionChanged += LogMessages_CollectionChanged;
        }

        Closing += OnLogWindowClosing;
        AppLogger.Log("Log window initialized.");
    }

    public void ForceClose()
    {
        _isForceClosing = true;
    }

    private void OnLogWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isForceClosing)
        {
            // Allow the window to close if the application is shutting down.
            return;
        }

        // Instead of closing, just hide the window. The main window can re-show it.
        e.Cancel = true;
        Hide();
        AppLogger.Log("Log window hidden.");

        // Notify that the window was hidden
        WindowHidden?.Invoke(this, EventArgs.Empty);
    }

    private void LogMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            // When a new log is added to the source collection...
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
            {
                var newText = new StringBuilder();
                foreach (LogEntry item in e.NewItems)
                {
                    newText.AppendLine(item.Message);
                }

                // ...append it to our TextBox.
                LogTextBox.AppendText(newText.ToString());
                LogTextBox.ScrollToEnd();
                break;
            }
            // When the source collection is cleared...
            case NotifyCollectionChangedAction.Reset:
                // ...clear our TextBox.
                LogTextBox.Clear();
                break;
        }
    }

    private void CopyLog_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Copy All button clicked.");
        try
        {
            if (LogTextBox.Text.Length > 0)
            {
                Clipboard.SetText(LogTextBox.Text);
                AppLogger.Log("Entire log content copied to clipboard.");
            }
            else
            {
                AppLogger.Log("Log is empty, nothing to copy.");
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to copy log to clipboard: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Failed to copy entire log to clipboard.");
        }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        AppLogger.Log("Clear button clicked. Clearing log messages from view.");
        // This will trigger the CollectionChanged event with a 'Reset' action,
        // which will then clear the TextBox.
        AppLogger.LogMessages.Clear();
    }

    private void CopySelection_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(LogTextBox.SelectedText)) return;

        AppLogger.Log("Copy Selection clicked.");
        try
        {
            Clipboard.SetText(LogTextBox.SelectedText);
            AppLogger.Log("Selected log text copied to clipboard.");
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Failed to copy selection to clipboard: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Failed to copy selected log text to clipboard.");
        }
    }

    private void LogTextBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        var hasSelection = !string.IsNullOrEmpty(LogTextBox.SelectedText);
        CopySelectionButton.IsEnabled = hasSelection;
        ContextMenuCopySelection.IsEnabled = hasSelection;
    }
}
