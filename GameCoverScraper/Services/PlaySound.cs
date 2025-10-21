using System.IO;
using System.Windows.Media;

namespace GameCoverScraper.Services;

public static class PlaySound
{
    private static readonly string SoundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio", "click.mp3");
    private static MediaPlayer? _mediaPlayer;

    public static void PlayClickSound()
    {
        try
        {
            AppLogger.Log("Attempting to play click sound.");
            if (!File.Exists(SoundPath))
            {
                AppLogger.Log($"Sound file not found at: {SoundPath}");
                _ = BugReport.LogErrorAsync(new FileNotFoundException($"Sound file not found: {SoundPath}"), "Sound file missing");
                return;
            }

            // Clean up the previous MediaPlayer instance if it exists
            CleanupMediaPlayer();

            // Create a new MediaPlayer instance
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed += MediaPlayer_MediaFailed;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;

            _mediaPlayer.Open(new Uri(SoundPath, UriKind.Absolute));
        }
        catch (Exception ex)
        {
            _ = BugReport.LogErrorAsync(ex, "Error in PlayClickSound");
        }
    }

    private static void CleanupMediaPlayer()
    {
        if (_mediaPlayer != null)
        {
            // Remove event handlers
            _mediaPlayer.MediaEnded -= MediaPlayer_MediaEnded;
            _mediaPlayer.MediaFailed -= MediaPlayer_MediaFailed;
            _mediaPlayer.MediaOpened -= MediaPlayer_MediaOpened;

            // Stop and close the player
            _mediaPlayer.Stop();
            _mediaPlayer.Close();

            // Set to null to allow garbage collection
            _mediaPlayer = null;
        }
    }

    private static void MediaPlayer_MediaEnded(object? sender, EventArgs e)
    {
        if (sender is MediaPlayer player)
        {
            player.Close();
            CleanupMediaPlayer();
        }
    }

    private static void MediaPlayer_MediaFailed(object? sender, ExceptionEventArgs e)
    {
        if (sender is MediaPlayer player)
        {
            player.Close();
            AppLogger.Log($"Failed to play sound: {e.ErrorException.Message}");
            _ = BugReport.LogErrorAsync(e.ErrorException, $"Failed to play sound: {SoundPath}");
            CleanupMediaPlayer();
        }
    }

    private static void MediaPlayer_MediaOpened(object? sender, EventArgs e)
    {
        AppLogger.Log("MediaPlayer opened, playing sound.");
        if (sender is MediaPlayer player)
        {
            player.Play();
        }
    }
}
