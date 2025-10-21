# 🎮 Scraping ROM Cover

A powerful Windows desktop application for automatically finding and downloading missing cover art for your retro gaming ROM collection. Supports both Bing and Google Image Search APIs to fetch high-quality game cover images.

## ✨ Features

- **🔍 Smart Search**: Automatically searches for game covers using cleaned ROM filenames
- **🎯 MAME Integration**: Leverages MAME database for accurate game titles and descriptions
- **📊 Batch Processing**: Scan entire ROM directories to identify missing covers
- **🖼️ Multiple Sources**: Support for both Bing and Google Image Search APIs
- **⚡ Real-time Preview**: Thumbnail previews with configurable sizes (100-500px)
- **🎨 Customizable UI**: Light/Dark themes with 20+ accent colors
- **📋 Missing Covers List**: Automatically generates a list of ROMs without cover art
- **🔧 Flexible Configuration**: Support for custom file extensions and search queries
- **📝 Detailed Logging**: Built-in log viewer for troubleshooting
- **🎵 Sound Feedback**: Optional audio feedback for user actions

## 📦 Supported File Types

By default, the application supports:
- **Archive formats**: ZIP, RAR, 7Z
- **Nintendo**: NES, SNES, Game Boy (GB, GBC, GBA), NDS, 3DS
- **Sega**: Genesis/Mega Drive (MD, SMD, GEN), 32X, Game Gear (GG), Master System
- **Other**: ROM, BIN, ISO, CHD, CDI, RVZ, NSP, XCI, WUA, WAD, CSO, LNK, BAT, EXE

You can easily add or remove supported extensions through the settings.

## 🚀 Getting Started

### Prerequisites

- Windows 10 or later
- .NET 9.0 SDK (automatically installed if using the provided executable)
- Valid API keys for either Bing or Google Image Search

### Installation

1. **Download the latest release** from the [Releases](https://github.com/yourusername/ScrapingRomCover/releases) page
2. **Extract** the archive to a folder of your choice
3. **Configure API keys** (see Setup section below)
4. **Run** `ScrapingRomCover.exe`

### API Setup

#### Option 1: Bing Image Search API
1. Go to [Azure Portal](https://portal.azure.com)
2. Create a Bing Search v7 resource
3. Copy your API key
4. Add it to `settings.xml`:
   ```xml
   <BingApiKey>your-bing-api-key-here</BingApiKey>
   ```

#### Option 2: Google Custom Search API
1. Go to [Google Cloud Console](https://console.cloud.google.com)
2. Enable the Custom Search JSON API
3. Create an API key
4. Create a Custom Search Engine for image search
5. Add both to `settings.xml`:
   ```xml
   <GoogleKey>your-google-api-key-here</GoogleKey>
   <GoogleSearchEngineId>your-search-engine-id-here</GoogleSearchEngineId>
   ```

### First Run Configuration

1. **Launch** the application
2. **Select your ROM folder** using the "Browse..." button
3. **Select your cover images folder** (where covers will be saved)
4. **Click "Check for Missing Images"** to scan for ROMs without covers
5. **Select a game** from the missing covers list to see search results
6. **Click on a cover image** to download it automatically

## 🎯 Usage Guide

### Basic Workflow

1. **Setup Directories**
    - ROM Folder: Where your game files are stored
    - Image Folder: Where you want cover images saved

2. **Scan for Missing Covers**
    - Click "Check for Missing Images"
    - The app will list all ROMs without corresponding PNG/JPG covers

3. **Find Covers**
    - Select a game from the missing covers list
    - The app automatically searches for cover images
    - Browse through the results

4. **Download Covers**
    - Click on the cover image you want
    - The image is automatically saved as `[gamename].png`
    - The game is removed from the missing covers list

### Advanced Features

#### Custom Search Queries
Add extra search terms in the "Extra Query" field:
- `"box art"` - for box art specifically
- `"front cover"` - for front covers only
- `"high resolution"` - for higher quality images

#### Theme Customization
Access through the menu:
- **View > Theme > Base Theme** - Switch between Light and Dark
- **View > Theme > Accent Colors** - Choose from 20+ color schemes
- **View > Set Thumbnail Size** - Adjust preview sizes (100-500px)

#### Search Engine Selection
Switch between Bing and Google via:
- **View > Select Search Engine**

## 🔧 Configuration

All settings are stored in `settings.xml` in the application folder:

```xml
<Settings>
    <ThumbnailSize>300</ThumbnailSize>
    <SearchEngine>Bing</SearchEngine>
    <BaseTheme>Light</BaseTheme>
    <AccentColor>Blue</AccentColor>
    <BingApiKey>your-key-here</BingApiKey>
    <GoogleKey>your-key-here</GoogleKey>
    <GoogleSearchEngineId>your-id-here</GoogleSearchEngineId>
    <SupportedExtensions>
        <Extension>zip</Extension>
        <Extension>nes</Extension>
        <!-- Add more as needed -->
    </SupportedExtensions>
</Settings>
```

## 🐛 Troubleshooting

### Common Issues

**"API Key is not set" error**
- Ensure your API keys are properly added to `settings.xml`
- Verify the keys are active and have sufficient quota

**No search results**
- Check your internet connection
- Try different search terms in "Extra Query"
- Switch between Bing and Google search engines

**Images not saving**
- Ensure the image folder has write permissions
- Check if files already exist (you'll be prompted to overwrite)

### Logs
Access detailed logs via:
- **View > Show/Hide Log Window**
- Log files are saved as `app.log` and `error.log` in the application folder

## 🙏 Acknowledgments

- **MahApps.Metro** for the beautiful WPF UI framework
- **Magick.NET** for image processing capabilities
- **MessagePack** for efficient binary serialization
- **MAME** team for the comprehensive arcade game database

## 📞 Support

- **Issues**: [GitHub Issues](https://github.com/yourusername/ScrapingRomCover/issues)
- **Discussions**: [GitHub Discussions](https://github.com/yourusername/ScrapingRomCover/discussions)
- **Donations**: [Support Development](https://www.purelogiccode.com/donate)

---

Made with ❤️ by [Pure Logic Code](https://www.purelogiccode.com)