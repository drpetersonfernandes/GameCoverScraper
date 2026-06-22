using System.IO;
using System.Windows.Media;

namespace GameCoverScraper.Services;

public class LocalAudioService : IAudioService
{
    private MediaPlayer? _mediaPlayer;
    private Uri? _soundUri;
    private bool _isSoundAvailable;

    public LocalAudioService()
    {
        var soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "audio", "click.mp3");

        if (File.Exists(soundPath))
        {
            try
            {
                if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => InitializeMediaPlayer(soundPath));
                }
                else
                {
                    InitializeMediaPlayer(soundPath);
                }
            }
            catch (Exception)
            {
                _isSoundAvailable = false;
                try
                {
                    _mediaPlayer?.Close();
                }
                catch
                {
                    // ignored
                }
            }
        }
        else
        {
            _isSoundAvailable = false;
        }
    }

    private void InitializeMediaPlayer(string soundPath)
    {
        _mediaPlayer = new MediaPlayer();
        _soundUri = new Uri(soundPath, UriKind.Absolute);
        _mediaPlayer.Open(_soundUri);
        _mediaPlayer.MediaFailed += OnMediaFailed;
        _isSoundAvailable = true;
    }

    private void OnMediaFailed(object? sender, ExceptionEventArgs e)
    {
        _isSoundAvailable = false;
    }

    public void PlayClickSound()
    {
        if (!_isSoundAvailable) return;

        try
        {
            _mediaPlayer?.Stop();
            _mediaPlayer?.Play();
        }
        catch
        {
            _isSoundAvailable = false;
        }
    }

    public void Dispose()
    {
        try
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.MediaFailed -= OnMediaFailed;
                _mediaPlayer.Close();
            }
        }
        catch
        {
            // ignored
        }

        GC.SuppressFinalize(this);
    }
}
