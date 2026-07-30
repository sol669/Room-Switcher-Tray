using Microsoft.UI.Dispatching;
using RoomSwitcherTray.Interop;
using RoomSwitcherTray.Models;
using System.Runtime.InteropServices;
using Windows.System;

namespace RoomSwitcherTray.Services;

public sealed class TrayService : IDisposable
{
    private const uint TrayMessage = NativeMethods.WM_APP + 1;
    private const uint ScenarioCommandBase = 2000;
    private const uint IdMonitorOff = 1001;
    private const uint IdHdr = 1002;
    private const uint IdDisplaySettings = 1003;
    private const uint IdSettings = 1004;
    private const uint IdExit = 1005;

    private readonly SettingsStore _settings;
    private readonly ScenarioService _scenarios;
    private readonly NativeMethods.WndProc _windowProc;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private nint _window;
    private NativeMethods.NOTIFYICONDATA _notifyData;
    private SettingsWindow? _settingsWindow;
    private IReadOnlyList<Scenario> _menuScenarios = [];

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

        _notifyData = new NativeMethods.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATA>(),
            hWnd = _window,
            uID = 1,
            uFlags = NativeMethods.NIF_MESSAGE | NativeMethods.NIF_ICON | NativeMethods.NIF_TIP,
            uCallbackMessage = TrayMessage,
            hIcon = NativeMethods.LoadIcon(nint.Zero, new nint(32512)),
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
        nint scenariosMenu = NativeMethods.CreatePopupMenu();
        _menuScenarios = _settings.Current.Scenarios.ToList();

        try
        {
            if (_menuScenarios.Count == 0)
            {
                NativeMethods.AppendMenu(scenariosMenu, NativeMethods.MF_STRING, 0,
                    Strings.Ru ? "(нет сценариев)" : "(no scenarios)");
            }
            else
            {
                for (int i = 0; i < _menuScenarios.Count; i++)
                {
                    Scenario scenario = _menuScenarios[i];
                    uint flags = NativeMethods.MF_STRING;
                    if (_settings.Current.ActiveScenarioId == scenario.Id)
                        flags |= NativeMethods.MF_CHECKED;
                    NativeMethods.AppendMenu(scenariosMenu, flags,
                        ScenarioCommandBase + (uint)i, scenario.Name);
                }
            }

            NativeMethods.AppendMenu(menu, NativeMethods.MF_POPUP, (nuint)scenariosMenu, Strings.Scenarios);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, 0, null);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdMonitorOff, Strings.MonitorOff);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdHdr, Strings.ToggleHdr);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdDisplaySettings, Strings.DisplaySettings);
            NativeMethods.AppendMenu(menu, NativeMethods.MF_STRING, IdSettings, Strings.Settings);
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
            NativeMethods.DestroyMenu(scenariosMenu);
            NativeMethods.DestroyMenu(menu);
        }
    }

    private void ExecuteCommand(uint command)
    {
        if (command >= ScenarioCommandBase &&
            command < ScenarioCommandBase + _menuScenarios.Count)
        {
            _ = ApplyScenarioAsync(_menuScenarios[(int)(command - ScenarioCommandBase)]);
            return;
        }

        switch (command)
        {
            case IdMonitorOff:
                NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST,
                    NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MONITORPOWER, new nint(2));
                break;
            case IdHdr:
                SendHdrShortcut();
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
        if (_settings.Current.Scenarios.Count == 0)
        {
            ShowSettings();
            return;
        }

        int current = _settings.Current.Scenarios.FindIndex(s =>
            s.Id == _settings.Current.ActiveScenarioId);
        Scenario next = _settings.Current.Scenarios[(current + 1) % _settings.Current.Scenarios.Count];
        await ApplyScenarioAsync(next);
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

        _settingsWindow = new SettingsWindow(_settings, this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Activate();
    }

    internal void Refresh()
    {
        _notifyData.uFlags = NativeMethods.NIF_TIP;
        _notifyData.szTip = BuildTooltip();
        NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_MODIFY, ref _notifyData);
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
        return activeName is null ? "Room Switcher Tray" : $"Room Switcher Tray — {activeName}";
    }

    private static void SendHdrShortcut()
    {
        NativeMethods.KeybdEvent(0x5B, 0, 0, 0);
        NativeMethods.KeybdEvent(0x12, 0, 0, 0);
        NativeMethods.KeybdEvent(0x42, 0, 0, 0);
        NativeMethods.KeybdEvent(0x42, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        NativeMethods.KeybdEvent(0x12, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        NativeMethods.KeybdEvent(0x5B, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
    }

    public void Dispose()
    {
        if (_window != nint.Zero)
        {
            NativeMethods.Shell_NotifyIcon(NativeMethods.NIM_DELETE, ref _notifyData);
            NativeMethods.DestroyWindow(_window);
            _window = nint.Zero;
        }
    }
}
