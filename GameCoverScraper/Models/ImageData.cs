using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameCoverScraper.models;

public class ImageData : INotifyPropertyChanged
{
    private int _imageWidth;
    private int _imageHeight;

    public required string? ImagePath { get; set; }
    public string ImageName { get; set; } = "Unknown Filename";
    public string ImageFileSize { get; set; } = "Unknown File Size";
    public string ImageEncodingFormat { get; set; } = "Unknown Encoding Format";

    public int ImageWidth
    {
        get => _imageWidth;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Image width cannot be negative.");

            _imageWidth = value;
        }
    }

    public int ImageHeight
    {
        get => _imageHeight;
        set
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Image height cannot be negative.");

            _imageHeight = value;
        }
    }

    public int ThumbnailWidth
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnPropertyChanged();
        }
    }

    public int ThumbnailHeight
    {
        get;
        set
        {
            if (field == value) return;

            field = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
