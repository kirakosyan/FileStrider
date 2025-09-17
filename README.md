# 🗂️ FileStrider

**Big File and Folder Discovery Tool for Mac and PC**

FileStrider is a powerful, cross-platform desktop application that helps you discover and manage large files and folders on your system. Whether you're running out of disk space or trying to optimize your storage, FileStrider makes it easy to identify what's taking up the most space.

## ✨ Features

- **🔍 Fast File System Scanning**: Efficiently scans your directories to find the largest files and folders
- **📊 File Type Analysis**: NEW! Analyze disk usage by file type with detailed statistics, percentages, and a visual breakdown panel
- **🎯 Top N Results**: Configurable number of top results to display (default: 20)
- **📈 Detailed Information**: Shows file/folder sizes, modification dates, and item counts
- **🖱️ Double-Click Integration**: Double-click any file or folder to open it in your system's file manager (Windows Explorer, macOS Finder, or Linux file manager)
- **📤 Enhanced Export**: Export scan results including file type statistics to CSV or JSON formats
- **⚙️ Flexible Configuration**: 
  - Include/exclude hidden files and folders
  - Set minimum file size filters
  - Exclude specific directories (node_modules, .git, etc.)
  - Control scan depth and concurrency
- **🔄 Real-time Progress**: Live updates during scanning with current path and statistics
- **❌ Cancellable Operations**: Stop long-running scans at any time
- **🎨 Modern UI**: Clean, intuitive three-column interface built with Avalonia UI

## 🖥️ System Requirements

- **Operating Systems**: Windows, macOS, Linux
- **.NET Runtime**: .NET 8.0 or later
- **Memory**: 512 MB RAM minimum (more recommended for large scans)
- **Storage**: 50 MB free disk space

## 🚀 Installation

### Prerequisites

1. Install [.NET 8.0 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Building from Source

1. Clone the repository:
   ```bash
   git clone https://github.com/kirakosyan/FileStrider.git
   cd FileStrider
   ```

2. Build the application:
   ```bash
   dotnet build
   ```

3. Run the application:
   ```bash
   dotnet run --project src/FileStrider.MauiApp
   ```

### Pre-built Releases

Download the latest release from the [Releases page](https://github.com/kirakosyan/FileStrider/releases).

## 📋 Usage

### Basic Scanning

1. **Select a Folder**: Click "Browse..." to choose the directory you want to scan, or use "Quick Scan" to scan the current directory
2. **Configure Options** (optional):
   - Set the number of top results to display
   - Include hidden files and folders
   - Set minimum file size threshold
3. **Start Scanning**: Click "▶️ Start Scan" to begin the analysis
4. **View Results**: The largest files, folders, and file type statistics will appear in the three panels

### Advanced Features

- **File Type Analysis**: View detailed breakdown of disk usage by file type and category
- **Double-Click to Open**: Double-click any file or folder in the results to open it in your system's file manager with the item highlighted
- **Enhanced Export**: Use "📄 Export CSV" or "📝 Export JSON" to save your scan results including file type statistics
- **Cancel Scans**: Use "❌ Cancel" to stop long-running operations
- **Tooltips**: Hover over items to see their full file paths

### File Type Categories

FileStrider automatically categorizes files into the following types:
- **Images**: jpg, png, gif, bmp, svg, heic, and more
- **Videos**: mp4, avi, mkv, mov, webm, and more  
- **Audio**: mp3, wav, flac, aac, ogg, and more
- **Documents**: pdf, doc, xls, ppt, txt, and more
- **Archives**: zip, rar, 7z, tar, gz, and more
- **Code**: cs, js, py, java, html, css, and more
- **Executables**: exe, msi, dmg, app, dll, and more

### Configuration Options

- **Top N**: Number of largest items to track (1-200)
- **Include Hidden**: Whether to include hidden files and directories
- **Min Size**: Minimum file size in bytes to include in results
- **Excluded Directories**: By default excludes `node_modules`, `.git`, and `Library/Caches`

## 🏗️ Architecture

FileStrider is built with a clean, modular architecture:

- **FileStrider.Core**: Core models and contracts
- **FileStrider.Scanner**: File system scanning engine
- **FileStrider.Platform**: Platform-specific services (file manager integration, clipboard)
- **FileStrider.Infrastructure**: Configuration and export services
- **FileStrider.MauiApp**: Avalonia-based UI application
- **FileStrider.Tests**: Unit tests

## 🔧 Development

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Git](https://git-scm.com/)

### Building

```bash
# Clone the repository
git clone https://github.com/kirakosyan/FileStrider.git
cd FileStrider

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run tests
dotnet test

# Run the application
dotnet run --project src/FileStrider.MauiApp
```

### Project Structure

```
FileStrider/
├── src/
│   ├── FileStrider.Core/          # Core models and interfaces
│   ├── FileStrider.Scanner/       # File system scanning logic
│   ├── FileStrider.Platform/      # Platform-specific services
│   ├── FileStrider.Infrastructure/# Configuration and export services
│   ├── FileStrider.MauiApp/       # Avalonia UI application
│   └── FileStrider.Tests/         # Unit tests
├── FileStrider.sln                # Solution file
└── README.md                      # This file
```

## 🧪 Testing

Run the test suite with:

```bash
dotnet test
```

The test suite includes unit tests for:
- File system scanning logic
- Top items tracking
- Export functionality
- Platform services

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request. For major changes, please open an issue first to discuss what you would like to change.

### Guidelines

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Make your changes
4. Add tests for new functionality
5. Ensure all tests pass (`dotnet test`)
6. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
7. Push to the branch (`git push origin feature/AmazingFeature`)
8. Open a Pull Request

## 📝 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- Built with [Avalonia UI](https://avaloniaui.net/) for cross-platform desktop development
- Uses [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) for MVVM pattern implementation
- Inspired by tools like WinDirStat, DaisyDisk, and ncdu

## 📞 Support

If you encounter any issues or have questions:

1. Check the [Issues page](https://github.com/kirakosyan/FileStrider/issues) for existing solutions
2. Create a new issue with detailed information about your problem
3. Include your operating system, .NET version, and steps to reproduce

---

**Made with ❤️ for developers and system administrators who need to manage disk space efficiently.**
