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