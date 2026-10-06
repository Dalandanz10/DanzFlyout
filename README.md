# DanzFlyout

A lightweight, modern Windows desktop utility and taskbar audio visualizer built with C# and WPF. It replaces the default Windows media and volume flyouts with responsive, theme-adaptive widgets and renders real-time audio waveforms directly above the taskbar.

---

## Features

- **Fluid Wave Taskbar Visualizer**: Real-time audio spectral capture using NAudio WASAPI loopback, rendering smooth dual-harmonic waveforms with soft-knee dynamic compression (`tanh`) to prevent taskbar overflow without flattening peaks.
- **Modern Media & Volume Flyouts**: Custom overlay widgets for master volume, lock keys (Caps Lock, Num Lock, Scroll Lock), and media track controls.
- **Adaptive Album Palette**: Dynamically samples active album artwork from Spotify, Apple Music, and web browsers to tint control elements and visualizer strokes.
- **Intelligent Fullscreen Suppression**: Win32 P/Invoke monitoring automatically suppresses flyouts during games and borderless video playback.
- **Low-Latency DSP Architecture**: 1024-point FFT processing on dedicated background threads with thread-safe snapshot rendering and automatic audio endpoint reconnection.

---

## Installation

Download the pre-compiled installer from the **[Releases](https://github.com/Dalandanz10/DanzFlyout/releases)** section:

1. Download `DanzFlyout_Setup_1.0.1.exe`.
2. Run the setup wizard to install the application.
3. DanzFlyout will run in your Windows system tray. Right-click the tray icon to open Settings or adjust placement.

---

## Building from Source

### Prerequisites
- Windows 10 (Build 19041+) or Windows 11
- [.NET 8.0 or 9.0 SDK](https://dotnet.microsoft.com/download)
- Visual Studio 2022 or Visual Studio Code with C# Dev Kit

### Build Instructions

```powershell
# 1. Clone the repository
git clone [https://github.com/Dalandanz10/DanzFlyout.git](https://github.com/Dalandanz10/DanzFlyout.git)
cd DanzFlyout

# 2. Restore dependencies
dotnet restore

# 3. Build Release version
dotnet build -c Release
```

---

## Tech Stack & Architecture

- **UI Framework**: WPF (.NET)
- **Audio Capture & DSP**: NAudio (WASAPI Loopback Capture, 1024-point FFT)
- **Native Interop**: Win32 API (`user32.dll`) via P/Invoke (Window styles, Z-ordering, Fullscreen detection)
- **Installer**: Inno Setup

---

## Author

**Danz (Dannel Bert Lomtong)**  
*IT Student & Developer*

- GitHub: [@Dalandanz10](https://github.com/Dalandanz10)

*DanzFlyout was built as an exploration into real-time audio DSP, multithreaded systems programming in .NET, and Windows desktop UX engineering.*

---

## License

This project is licensed under the MIT License.