using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using GameCoverScraper.Models;
using Clipboard = System.Windows.Clipboard;
using ContextMenu = System.Windows.Controls.ContextMenu;
using Image = System.Windows.Controls.Image;
using MenuItem = System.Windows.Controls.MenuItem;
using MessageBox = System.Windows.MessageBox;

namespace GameCoverScraper.Services;

public static class ButtonFactory
{
    public static Task<SimilarityCalculationResult> CreateSimilarImagesCollectionAsync(
        string selectedRomFileName,
        string imageFolderPath,
        double similarityThreshold,
        string similarityAlgorithm,
        CancellationToken cancellationToken,
        Action<ImageData>? onImageLoaded = null)
    {
        return SimilarityCalculator.CalculateSimilarityAsync(
            selectedRomFileName,
            imageFolderPath,
            similarityThreshold,
            similarityAlgorithm,
            cancellationToken,
            onImageLoaded: onImageLoaded);
    }

    public static ContextMenu CreateContextMenu(string imagePath, Action<string?> useImageAction, ContextMenu? existingMenu = null)
    {
        if (string.IsNullOrEmpty(imagePath))
        {
            return new ContextMenu();
        }

        if (existingMenu is { Items.Count: > 0 })
        {
            foreach (var item in existingMenu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    menuItem.CommandParameter = imagePath;
                }
            }

            return existingMenu;
        }

        var contextMenu = existingMenu ?? new ContextMenu();

        var useThisImageIcon = new Image
        {
            Source = CreateFrozenBitmapImage("pack://application:,,,/images/usethis.png"),
            Width = 16,
            Height = 16,
            Margin = new Thickness(2)
        };
        var useThisImageMenuItem = new MenuItem
        {
            Header = "Use This Image",
            Command = new DelegateCommand(p => useImageAction.Invoke(p as string)),
            CommandParameter = imagePath,
            Icon = useThisImageIcon
        };
        contextMenu.Items.Add(useThisImageMenuItem);

        var copyIcon = new Image
        {
            Source = CreateFrozenBitmapImage("pack://application:,,,/images/copy.png"),
            Width = 16,
            Height = 16,
            Margin = new Thickness(2)
        };
        var copyMenuItem = new MenuItem
        {
            Header = "Copy Image Filename",
            Command = CopyImageFilenameCommand,
            CommandParameter = imagePath,
            Icon = copyIcon
        };
        contextMenu.Items.Add(copyMenuItem);

        var openLocationIcon = new Image
        {
            Source = CreateFrozenBitmapImage("pack://application:,,,/images/folder.png"),
            Width = 16,
            Height = 16,
            Margin = new Thickness(2)
        };
        var openLocationMenuItem = new MenuItem
        {
            Header = "Open File Location",
            Command = OpenFileLocationCommand,
            CommandParameter = imagePath,
            Icon = openLocationIcon
        };
        contextMenu.Items.Add(openLocationMenuItem);

        return contextMenu;
    }

    private static ICommand CopyImageFilenameCommand { get; } = new DelegateCommand(param =>
    {
        if (param is not string imagePath) return;

        var filenameWithoutExtension = Path.GetFileNameWithoutExtension(imagePath);

        try
        {
            Clipboard.SetText(filenameWithoutExtension);
            MessageBox.Show($"Filename '{filenameWithoutExtension}' copied to clipboard!",
                "Copied", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            MessageBox.Show("Could not copy to clipboard. It might be in use by another application.",
                "Clipboard Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    });

    private static ICommand OpenFileLocationCommand { get; } = new DelegateCommand(static param =>
    {
        if (param is not string imagePath || string.IsNullOrEmpty(imagePath))
        {
            return;
        }

        if (File.Exists(imagePath))
        {
            var processStartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{imagePath}\"",
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(processStartInfo);
        }
    });

    private static BitmapImage CreateFrozenBitmapImage(string uri)
    {
        var bitmap = new BitmapImage(new Uri(uri));
        if (bitmap.CanFreeze)
            bitmap.Freeze();

        return bitmap;
    }
}
