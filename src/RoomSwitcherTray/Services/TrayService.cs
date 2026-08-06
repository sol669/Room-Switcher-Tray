using RoomSwitcherTray.Interop;
using RoomSwitcherTray.Models;
using System.Runtime.InteropServices;
using Windows.System;

namespace RoomSwitcherTray.Services;

public sealed class TrayService : IDisposable
{
    private const uint TrayMessage = NativeMethods.WM_APP + 1;
    private const uint IdNextScenario = 1000;
    private const uint IdMonitorOff = 1001;
    private const uint IdDisplaySettings = 1003;
    private const uint IdSettings = 1004;
    private const uint IdExit = 1005;
    private const uint ScenarioCommandBase = 2000;
    private const uint DisplayCommandBase = 3000;
    private const uint AudioCommandBase = 4000;

    private readonly SettingsStore _settings;
    private readonly ScenarioService _scenarios;
    private readonly NativeMethods.WndProc _windowProc;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private nint _window;
    private nint _trayIcon;
    private NativeMethods.NOTIFYICONDATA _notifyData;
    private SettingsWindow? _settingsWindow;
    private IReadOnlyList<Scenario> _menuScenarios = [];
    private IReadOnlyList<DisplayDevice> _menuDisplays = [];
    private IReadOnlyList<AudioDevice> _menuAudioDevices = [];
    private Scenario? _nextScenario;

    public TrayService(SettingsStore settings, ScenarioService scenarios)
    {
        _settings = settings;
        _scenarios = scenarios;
        _windowProc = WindowProc;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    public void Initialize()
    {
        const string className = "sol669.RoomSwitcherTray.TrayWindow";
        nint instance = NativeMethods.GetModuleHandle(null);
        var windowClass = new NativeMethods.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _windowProc,
            hInstance = instance,
            lpszClassName = className
        };
        NativeMethods.RegisterClassEx(ref windowClass);
        _window = NativeMethods.CreateWindowEx(0, className, "Room Switcher Tray", 0,
            0, 0, 0, 0, nint.Zero, nint.Zero, instance, nint.Zero);

        _trayIcon = CreateCurrentIcon();
        _notifyData = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _window,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = TrayMessage,
            hIcon = _trayIcon,
            szTip = BuildTooltip(),
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_ADD, ref _notifyData);
    }

    private nint WindowProc(nint hWnd, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == TrayMessage)
            {
                uint mouseMessage = unchecked((uint)lParam.ToInt64());
                if (mouseMessage == NativeMethods.WM_RBUTTONUP)
                    ShowMenu();
                else if (mouseMessage == NativeMethods.WM_LBUTTONDBLCLK)
                    _dispatcher.TryEnqueue(ApplyNextScenario);
                return nint.Zero;
            }
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
        }
        return NativeMethods.DefWindowProc(hWnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        NativeTheme.Apply(_settings.Current.Theme, _window);
        nint menu = NativeMethods.CreatePopupMenu();
        _menuScenarios = _settings.Current.Scenarios.ToList();
        _menuDisplays = App.Displays.GetDisplays().Where(d => d.IsActive).ToList();
        IReadOnlyList<AudioDevice> audioDevices = App.Audio.GetRenderDevices();
        AudioDevice? currentAudio = audioDevices.FirstOrDefault(d => d.IsDefault);
        _menuAudioDevices = audioDevices.Where(d => !d.IsDefault).ToList();
        _nextScenario = GetNextScenario();

        try
        {
            if (_nextScenario is not null)
            {
                NativeMethods.AppendMenu(menu,
                    NativeMethods.MF_STRING | NativeMethods.MF_DEFAULT,
                    IdNextScenario, Strings.SwitchTo(_nextScenario.Name));
            }
            else
            {
                NativeMethods.AppendMenu(menu,
                    NativeMethods.MF_STRING | NativeMethods.MF_GRAYED,
                    0, Strings.NoScenarios);
            }

            for (int i = 0; i < _menuScenarios.Count; i++)
            {
                Scenario scenario = _menuScenarios[i];
                if (_nextScenario?.Id == scenario.Id)
                    continue;
                uint flags = NativeMethods.MF_STRING;
                if (_settings.Current.ActiveScenarioId == scenario.Id)
                    flags |= NativeMethods.MF_CHECKED;
                NativeMethods.AppendMenu(menu, flags,
                    ScenarioCommandBase + (uint)i, scenario.Name);
            }

            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);

            for (int i = 0; i < _menuDisplays.Count; i++)
            {
                DisplayDevice display = _menuDisplays[i];
                string title = $"{_settings.DisplayName(display)}\t{FormatDisplayStatus(display)}";
                if (display.HdrSupported)
                {
                    nint hdrMenu = NativeMethods.CreatePopupMenu();
                    NativeMethods.AppendMenu(hdrMenu, NativeMethods.MF_STRING,
                        DisplayCommandBase + (uint)i,
                        display.HdrEnabled ? Strings.DisableHdr : Strings.EnableHdr);
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_POPUP,
                        (nuint)hdrMenu, title);
                }
                else
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 0, title);
                }
            }

            if (currentAudio is not null)
            {
                string title = $"{_settings.AudioName(currentAudio)}\t{currentAudio.VolumePercent}%";
                if (_menuAudioDevices.Count > 0)
                {
                    nint audioMenu = NativeMethods.CreatePopupMenu();
                    for (int i = 0; i < _menuAudioDevices.Count; i++)
                        NativeMethods.AppendMenu(audioMenu, NativeMethods.MF_STRING,
                            AudioCommandBase + (uint)i,
                            _settings.AudioName(_menuAudioDevices[i]));
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_POPUP,
                        (nuint)audioMenu, title);
                }
                else
                {
                    NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, 0, title);
                }
            }

            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING,
                IdMonitorOff, Strings.MonitorOff);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING,
                IdDisplaySettings, Strings.DisplaySettings);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING,
                IdSettings, Strings.Settings);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdExit, Strings.Exit);

            NativeMethods.GetCursorPos(out NativeMethods.POINT point);
            NativeMethods.SetForegroundWindow(_window);
            uint command = NativeMethods.TrackPopupMenu(menu,
                NativeMethods.TPM_RIGHTBUTTON | NativeMethods.TPM_RETURNCMD,
                point.X, point.Y, 0, _window, nint.Zero);
            NativeMethods.PostMessage(_window, 0, 0, 0);
            _dispatcher.TryEnqueue(() => ExecuteCommand(command));
        }
        finally
        {
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void ExecuteCommand(uint command)
    {
        if (command == 0)
            return;

        if (command == IdNextScenario && _nextScenario is not null)
        {
            _ = ApplyScenarioAsync(_nextScenario);
            return;
        }

        if (command >= ScenarioCommandBase &&
            command < ScenarioCommandBase + _menuScenarios.Count)
        {
            _ = ApplyScenarioAsync(_menuScenarios[(int)(command - ScenarioCommandBase)]);
            return;
        }

        if (command >= DisplayCommandBase &&
            command < DisplayCommandBase + _menuDisplays.Count)
        {
            DisplayDevice display = _menuDisplays[(int)(command - DisplayCommandBase)];
            try
            {
                App.Displays.SetHdr(display, !display.HdrEnabled);
                Refresh();
            }
            catch (Exception ex)
            {
                SettingsStore.Log(ex);
                ShowNotification(ex.Message, false);
            }
            return;
        }

        if (command >= AudioCommandBase &&
            command < AudioCommandBase + _menuAudioDevices.Count)
        {
            AudioDevice device = _menuAudioDevices[(int)(command - AudioCommandBase)];
            if (!App.Audio.SetDefault(device.Id, out string? error))
                ShowNotification($"{Strings.AudioSwitchFailed} {error}", false);
            Refresh();
            return;
        }

        switch (command)
        {
            case IdMonitorOff:
                NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MONITORPOWER, new nint(2));
                break;
            case IdDisplaySettings:
                _ = Launcher.LaunchUriAsync(new Uri("ms-settings:display"));
                break;
            case IdSettings:
                ShowSettings();
                break;
            case IdExit:
                App.Quit();
                break;
        }
    }

    private async void ApplyNextScenario()
    {
        Scenario? next = GetNextScenario();
        if (next is null)
        {
            ShowSettings();
            return;
        }
        await ApplyScenarioAsync(next);
    }

    private Scenario? GetNextScenario()
    {
        if (_settings.Current.Scenarios.Count == 0)
            return null;
        int current = _settings.Current.Scenarios.FindIndex(s =>
            s.Id == _settings.Current.ActiveScenarioId);
        return _settings.Current.Scenarios[(current + 1) % _settings.Current.Scenarios.Count];
    }

    internal async Task ApplyScenarioAsync(Scenario scenario)
    {
        ScenarioApplyResult result = await _scenarios.ApplyAsync(scenario);
        ShowNotification(result.Message, result.Success);
        Refresh();
        _settingsWindow?.RefreshAfterExternalChange();
    }

    public void ShowSettings()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        try
        {
            _settingsWindow = new SettingsWindow(_settings, this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Activate();
        }
        catch (Exception ex)
        {
            _settingsWindow = null;
            SettingsStore.Log(ex);
            ShowNotification(Strings.SettingsOpenFailed, false);
        }
    }

    internal void Refresh()
    {
        nint newIcon = CreateCurrentIcon();
        nint oldIcon = _trayIcon;
        _trayIcon = newIcon;
        _notifyData.hIcon = newIcon;
        _notifyData.uFlags = NativeMethods.NIF_TIP | NativeMethods.NIF_ICON;
        _notifyData.szTip = BuildTooltip();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyData);
        if (oldIcon != nint.Zero)
            NativeMethods.DestroyIcon(oldIcon);
    }

    internal void ShowNotification(string message, bool success)
    {
        _notifyData.uFlags = NativeMethods.NIF_INFO;
        _notifyData.szInfoTitle = "Room Switcher Tray";
        _notifyData.szInfo = message;
        _notifyData.dwInfoFlags = success ? NativeMethods.NIIF_INFO : NativeMethods.NIIF_ERROR;
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyData);
    }

    private string BuildTooltip()
    {
        string? activeName = _settings.Current.Scenarios.FirstOrDefault(s =>
            s.Id == _settings.Current.ActiveScenarioId)?.Name;
        Scenario? next = GetNextScenario();
        if (activeName is null)
            return "Room Switcher Tray";
        return next is null
            ? activeName
            : $"{activeName}\n{Strings.DoubleClick}: {next.Name}";
    }

    private static string FormatDisplayStatus(DisplayDevice display)
    {
        string hz = Strings.Ru ? "Гц" : "Hz";
        return $"{display.Width}×{display.Height} · {Math.Round(display.RefreshRate):0} {hz} · " +
               (display.HdrEnabled ? "HDR" : "SDR");
    }

    private nint CreateCurrentIcon()
    {
        string key = _settings.Current.Scenarios.FirstOrDefault(s =>
            s.Id == _settings.Current.ActiveScenarioId)?.IconKey ?? "monitor";
        return TrayIconFactory.Create(key);
    }

    public void Dispose()
    {
        if (_window != nint.Zero)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _notifyData);
            NativeMethods.DestroyWindow(_window);
            _window = nint.Zero;
        }
        if (_trayIcon != nint.Zero)
        {
            NativeMethods.DestroyIcon(_trayIcon);
            _trayIcon = nint.Zero;
        }
    }
}

