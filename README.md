# Room Switcher Tray

Room Switcher Tray is a native Windows 10/11 tray utility for switching a
complete display and audio setup with one click.

The project is an early working version (`0.1.0`). It supports:

- user-defined scenarios;
- stable display and Core Audio endpoint identifiers;
- selecting enabled displays, the primary display, and default audio output;
- a native Win32 tray menu with the active scenario marked;
- a compact WinUI 3 settings window;
- Russian and English UI;
- system, light, and dark themes;
- JSON settings in `%LOCALAPPDATA%\sol669\Room Switcher Tray`;
- framework-dependent and self-contained Windows x64 builds.

> Display arrangement, resolution, scale, orientation, and refresh rate remain
> under Windows control.

## Build

Requirements:

- Windows 10 version 1809 or newer;
- .NET 8 SDK;
- Visual Studio 2022 with Windows application development tools, or the
  equivalent MSBuild environment.

```powershell
dotnet restore Room-Switcher-Tray.sln
dotnet build Room-Switcher-Tray.sln -c Release -p:Platform=x64
```

The default-audio switch is isolated in `AudioDeviceService` because Windows
does not expose a supported public API for changing the default endpoint.

## Status

Hardware validation with multiple physical displays and audio endpoints is
required before the first stable release.

Author: [sol669](https://github.com/sol669)
