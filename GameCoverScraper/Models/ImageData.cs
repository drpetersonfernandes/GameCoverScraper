using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameCoverScraper.models;

public class ImageData : INotifyPropertyChanged
{
    public required string? ImagePath { get; set; }
    public string ImageName { get; set; } = "Unknown Filename";
    public string ImageFileSize { get; set; } = "Unknown File Size";
    public string ImageEncodingFormat { get; set; } = "Unknown Encoding Format";
    public int ImageWidth { get; set; }
    public int ImageHeight { get; set; }

    private int _thumbnailWidth;

    public int ThumbnailWidth
    {
        get => _thumbnailWidth;
        set
        {
            if (_thumbnailWidth == value) return;

            _thumbnailWidth = value;
            OnPropertyChanged();
        }
    }

    private int _thumbnailHeight;

    public int ThumbnailHeight
    {
        get => _thumbnailHeight;
        set
        {
            if (_thumbnailHeight == value) return;

            _thumbnailHeight = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}