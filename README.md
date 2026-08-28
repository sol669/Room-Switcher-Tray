# RoomSwitcher

RoomSwitcher is a native Windows 10/11 tray utility for switching a
complete display and audio setup with one click.

The current Core test build (`0.7.1`) supports:

- user-defined scenarios;
- stable display and Core Audio endpoint identifiers;
- selecting one to four enabled displays and a default audio output;
- a native tray menu with scenarios, Ctrl + Space, live active-display status,
  HDR controls, and audio volume status;
- a temporary native settings window with separate General and Scenarios pages;
- optional Windows sign-in launch and a selectable startup scenario rule;
- optional fixed scenario audio volume (including 0% for mute);
- a shortcut from the scenario editor to Windows Display settings;
- configurable global shortcut for switching to the next scenario;
- user-defined display and audio device names.
- a safe RDP tray mode that shows only live remote-session status and mute.
- a WinUI settings shell with theme, Russian/English, and scenario icon choices.
- JSON settings in `%LOCALAPPDATA%\sol669\Room Switcher Tray`;
- a self-contained Windows x64 build and small update-only archives.

> Display arrangement, resolution, scale, orientation, and refresh rate remain
> under Windows control.

Core supports up to four displays in a single scenario. The tray icon tooltip
contains only the name of the active scenario.

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
