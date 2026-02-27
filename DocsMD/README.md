# NirvanaRemap

A cross-platform **gamepad remapping tool** built with **Avalonia UI** that enables users to customize physical gamepad inputs and emit them as virtual Xbox 360 controllers via ViGEm.

## 🎯 Purpose

NirvanaRemap allows you to:
- **Remap physical gamepad inputs** to different virtual outputs
- **Support both Gamepad (XInput) and Joystick (DInput)** devices
- **Hide physical controllers from games** using HidHide integration
- **Run in GUI or headless mode** for different use cases
- **Customize button/axis mappings** with persistent JSON storage

Perfect for users with non-standard controllers, accessibility needs, or those wanting to standardize inputs across games.

## ⚙️ Prerequisites

### Required Software
- **Windows 10/11** (primary platform due to ViGEm and HidHide dependencies)
- **.NET 9.0 SDK** or higher
- **ViGEm Bus Driver** ([download here](https://github.com/nefarius/ViGEmBus/releases))
- **HidHide** (optional, for hiding physical devices) ([download here](https://github.com/nefarius/HidHide/releases))

### Hardware
- At least one physical gamepad or joystick

## 🚀 How to Run

### 1. Clone and Build

```powershell
# Clone the repository
git clone <repository-url>
cd RemapNirvana

# Restore dependencies
dotnet restore

# Build the solution
dotnet build --configuration Release
```

### 2. Run the Application

#### GUI Mode (Default)
```powershell
dotnet run --project Avalonia
```

This launches the Avalonia UI interface where you can:
- View detected gamepads
- Create and manage mapping profiles
- Test remappings in real-time

#### Headless Mode
```powershell
dotnet run --project Avalonia -- --raw
```

Headless mode runs without a UI, directly capturing physical inputs and emitting them via ViGEm. Useful for running as a background service.

### 3. First-Time Setup

1. **Install ViGEm Bus Driver** first
2. Run NirvanaRemap (it will auto-detect your physical controller)
3. (Optional) Configure HidHide to hide your physical device from games
4. Create a mapping profile or use the default configuration

## 📁 Project Structure

```
RemapNirvana/
├── Avalonia/          # Presentation layer (UI, Views, ViewModels)
├── ApplicationLayer/  # Business logic orchestration
├── Core/              # Domain layer (entities, interfaces)
├── Infrastructure/    # External adapters (ViGEm, HidHide, XInput)
└── README.md          # This file
```

For detailed architecture information, see [ARCHITECTURE.md](ARCHITECTURE.md).

## 🔧 Configuration

Mapping profiles are stored as JSON files in:
```
%APPDATA%/NirvanaRemap/mappings/
```

You can edit these manually or use the built-in UI mapper.

## 🛠️ Technology Stack

- **UI**: Avalonia 11.3.4 (cross-platform XAML framework)
- **Input Capture**: SDL3 (ppy.SDL3-CS 2025.816.0)
- **Virtual Controller**: ViGEm Client 1.21.256
- **Device Hiding**: HidHide CLI integration
- **Architecture**: Clean Architecture + MVVM
- **Language**: C# (.NET 9.0)

## 📝 Common Issues

### "ViGEm not found" error
- Ensure ViGEm Bus Driver is installed
- Restart your computer after installation

### Controller not detected
- Check if the controller is recognized in Windows Device Manager
- Try disconnecting and reconnecting the device
- Check application logs in `nirvana-input_main.log`

### Physical controller still visible in games
- Install and configure HidHide
- Ensure NirvanaRemap is in HidHide's whitelist
- Enable hiding in the application

## 📖 Further Reading

- [ARCHITECTURE.md](ARCHITECTURE.md) - Detailed system design and flow diagrams
- [CONTRIBUTING.md](CONTRIBUTING.md) - Guidelines for contributors
- [API_REFERENCE.md](API_REFERENCE.md) - Technical API documentation

## 📄 License

This project uses third-party libraries with their respective licenses:
- Avalonia UI (MIT)
- ViGEm Client (MIT)
- SDL3-CS (Zlib)

## 🤝 Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines on code style, testing, and pull requests.
