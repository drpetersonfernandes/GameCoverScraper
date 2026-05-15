using System.IO;
using NAudio.Wave;

namespace GameCoverScraper.Services;

public static class PlaySound
{
    private static readonly string SoundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio", "click.mp3");
    private static readonly object Lock = new();
    private static WaveOutEvent? _waveOut;

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

            lock (Lock)
            {
                CleanupAudioPlayer();

                _waveOut = new WaveOutEvent();
                _waveOut.PlaybackStopped += WaveOut_PlaybackStopped;

                var audioFile = new Mp3FileReader(SoundPath);
                _waveOut.Init(audioFile);
                _waveOut.Play();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Log($"Error playing sound: {ex.Message}");
            _ = BugReport.LogErrorAsync(ex, "Error in PlayClickSound");
        }
    }

    public static void Shutdown()
    {
        lock (Lock)
        {
            CleanupAudioPlayer();
        }
    }

    private static void CleanupAudioPlayer()
    {
        if (_waveOut != null)
        {
            _waveOut.PlaybackStopped -= WaveOut_PlaybackStopped;
            _waveOut.Stop();
            _waveOut.Dispose();
            _waveOut = null;
        }
    }

    private static void WaveOut_PlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (Lock)
        {
            CleanupAudioPlayer();
        }
    }
}
