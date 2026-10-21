# DanzFlyout

A lightweight, modern Windows desktop utility and taskbar audio visualizer built with C# and WPF. DanzFlyout provides custom media, volume, and lock-key flyouts, plus a real-time audio visualizer directly above the Windows taskbar.

![DanzFlyout Preview](Assets/preview.gif)

## Features

- **Fluid Wave Taskbar Visualizer** — Real-time audio capture using NAudio WASAPI loopback, with smooth waveforms and dynamic compression to help keep the visualizer within the taskbar area.
- **Modern Media & Volume Flyouts** — Custom widgets for master volume, Caps Lock / Num Lock / Scroll Lock, and media playback controls.
- **Configurable Taskbar Media Controls** — Optional previous, play/pause, and next controls beside the taskbar album artwork.
- **Adaptive Album Palette** — Uses active album artwork to adapt accent colors where supported.
- **Fullscreen Suppression** — Detects fullscreen applications and suppresses flyouts when appropriate.
- **Audio Processing** — Processes audio on background threads and reconnects to the audio endpoint when needed.
- **Settings** — Configure supported flyouts and taskbar visualizer options from the app's settings window.

## Installation

Download the latest installer from the [GitHub Releases](https://github.com/Dalandanz10/DanzFlyout/releases) page.

1. Download `DanzFlyout_Setup_1.0.5.exe` from the latest release.
2. Run the setup wizard.
3. DanzFlyout runs in the system tray. Right-click its tray icon to access Settings and other options.

## Building from Source

### Prerequisites

- Windows 10 (build 19041 or later) or Windows 11
- .NET 8 SDK
- Visual Studio 2022 or Visual Studio Code with the C# Dev Kit (optional)

### Build Instructions

```powershell
# Clone the repository
git clone https://github.com/Dalandanz10/DanzFlyout.git
cd DanzFlyout

# Restore dependencies
dotnet restore

# Build the Release configuration
dotnet build -c Release
```

To create a publish folder for the installer, run this from the project directory:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o .\Publish_1.0.5
```

Then compile `DanzFlyoutInstaller.iss` with Inno Setup 6.

## Tech Stack

- **UI:** WPF on .NET 8
- **Audio capture:** NAudio WASAPI loopback
- **Native integration:** Win32 APIs via P/Invoke
- **Installer:** Inno Setup

## Author

**Danz (Dannel Bert Lomtong)** — IT Student & Developer

- GitHub: [@Dalandanz10](https://github.com/Dalandanz10)

DanzFlyout was built as an exploration of real-time audio processing, multithreaded .NET programming, and Windows desktop UX engineering.

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) if available.
