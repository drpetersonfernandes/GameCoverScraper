using FluentAssertions;
using GameCoverScraper.Managers;
using Xunit;

namespace GameCoverScraper.Tests.Managers;

[Collection("SettingsManager")]
public class SettingsManagerTests : IDisposable
{
    private readonly string _originalSettingsPath;
    private readonly string _tempSettingsPath;

    public SettingsManagerTests()
    {
        // SettingsManager uses AppDomain.CurrentDomain.BaseDirectory, so we work in that directory.
        _originalSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml");
        _tempSettingsPath = _originalSettingsPath + ".backup";

        // Backup existing settings.xml if present
        if (File.Exists(_originalSettingsPath))
        {
            File.Copy(_originalSettingsPath, _tempSettingsPath, true);
            File.Delete(_originalSettingsPath);
        }
    }

    public void Dispose()
    {
        // Restore original settings.xml
        if (File.Exists(_originalSettingsPath))
        {
            File.Delete(_originalSettingsPath);
        }

        if (File.Exists(_tempSettingsPath))
        {
            File.Copy(_tempSettingsPath, _originalSettingsPath, true);
            File.Delete(_tempSettingsPath);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ThumbnailSizeSetWithinRangeShouldUpdateValue()
    {
        var settings = new SettingsManager
        {
            ThumbnailSize = 200
        };

        settings.ThumbnailSize.Should().Be(200);
    }

    [Theory]
    [InlineData(49)]
    [InlineData(801)]
    public void ThumbnailSizeSetOutsideRangeShouldThrowArgumentOutOfRangeException(int invalidSize)
    {
        var settings = new SettingsManager();

        var act = () => { settings.ThumbnailSize = invalidSize; };

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("value");
    }

    [Fact]
    public void DefaultValuesShouldBeCorrect()
    {
        var settings = new SettingsManager();

        settings.ThumbnailSize.Should().Be(300);
        settings.SearchEngine.Should().Be("BingWeb");
        settings.BaseTheme.Should().Be("Light");
        settings.AccentColor.Should().Be("Blue");
        settings.UseMameDescriptions.Should().BeFalse();
        settings.GoogleSearchEngineId.Should().Be("d30e97188f5914611");
        settings.SupportedExtensions.Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoadSettingsShouldPersistValues()
    {
        var settings = new SettingsManager
        {
            ThumbnailSize = 400,
            SearchEngine = "Google",
            BaseTheme = "Dark",
            AccentColor = "Red",
            UseMameDescriptions = true,
            GoogleKey = "test-key",
            GoogleSearchEngineId = "test-engine-id"
        };

        settings.SaveSettings();

        var loadedSettings = new SettingsManager();
        loadedSettings.LoadSettings();

        loadedSettings.ThumbnailSize.Should().Be(400);
        loadedSettings.SearchEngine.Should().Be("Google");
        loadedSettings.BaseTheme.Should().Be("Dark");
        loadedSettings.AccentColor.Should().Be("Red");
        loadedSettings.UseMameDescriptions.Should().BeTrue();
        loadedSettings.GoogleKey.Should().Be("test-key");
        loadedSettings.GoogleSearchEngineId.Should().Be("test-engine-id");
    }

    [Fact]
    public void LoadSettingsWhenFileDoesNotExistShouldCreateDefaults()
    {
        if (File.Exists(_originalSettingsPath))
        {
            File.Delete(_originalSettingsPath);
        }

        var settings = new SettingsManager();
        settings.LoadSettings();

        settings.ThumbnailSize.Should().Be(300);
        settings.SupportedExtensions.Should().NotBeEmpty();
        File.Exists(_originalSettingsPath).Should().BeTrue();
    }
}
