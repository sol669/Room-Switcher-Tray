using RoomSwitcherTray.Core.Interop;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core.Services;

public sealed class TrayService : IDisposable
{
    private const uint TrayMessage = TrayNative.WM_APP + 1;
    private const uint SwitchCommand = 1000;
    private const uint ScenarioCommandBase = 2000;
    private const uint SettingsCommand = 3000;
    private const uint ExitCommand = 3001;

    private readonly SettingsStore _settings;
    private readonly ScenarioService _scenarios;
    private readonly TrayNative.WindowProcedure _windowProcedure;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private nint _window;
    private nint _icon;
    private TrayNative.NOTIFYICONDATA _notifyData;
    private SettingsWindow? _settingsWindow;

    public TrayService(SettingsStore settings, ScenarioService scenarios)
    {
        _settings = settings;
        _scenarios = scenarios;
        _windowProcedure = WindowProc;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    public void Initialize()
    {
        const string className = "sol669.RoomSwitcherTray.Core.TrayWindow";
        nint instance = TrayNative.GetModuleHandle(null);
        var windowClass = new TrayNative.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<TrayNative.WNDCLASSEX>(),
            lpfnWndProc = _windowProcedure,
            hInstance = instance,
            lpszClassName = className
        };
        TrayNative.RegisterClassEx(ref windowClass);
        _window = TrayNative.CreateWindowEx(0, className, "Room Switcher Tray", 0,
            0, 0, 0, 0, nint.Zero, nint.Zero, instance, nint.Zero);

        _icon = TrayIconFactory.Create(GetActiveNumber());
        _notifyData = new TrayNative.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<TrayNative.NOTIFYICONDATA>(),
            hWnd = _window,
            uID = 1,
            uFlags = TrayNative.NIF_MESSAGE | TrayNative.NIF_ICON | TrayNative.NIF_TIP,
            uCallbackMessage = TrayMessage,
            hIcon = _icon,
            szTip = BuildTooltip(),
            szInfo = string.Empty,
            szInfoTitle = string.Empty
        };
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_ADD, ref _notifyData);
    }

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == TrayMessage)
            {
                uint mouseMessage = unchecked((uint)lParam.ToInt64());
                if (mouseMessage == TrayNative.WM_RBUTTONUP)
                    ShowMenu();
                else if (mouseMessage == TrayNative.WM_LBUTTONDBLCLK)
                    _dispatcher.TryEnqueue(ApplyNext);
                return nint.Zero;
            }
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
        }
        return TrayNative.DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        nint menu = TrayNative.CreatePopupMenu();
        try
        {
            if (_settings.IsConfigured)
            {
                ScenarioDefinition nextScenario = GetNextScenario();
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING | TrayNative.MF_DEFAULT,
                    SwitchCommand, $"Переключить на «{nextScenario.Name}»");
                for (int index = 0; index < _settings.Current.Scenarios.Count; index++)
                {
                    ScenarioDefinition scenario = _settings.Current.Scenarios[index];
                    TrayNative.AppendMenu(menu, TrayNative.MF_STRING |
                        (_settings.Current.ActiveScenarioId == scenario.Id
                            ? TrayNative.MF_CHECKED : 0),
                        ScenarioCommandBase + (uint)index, scenario.Name);
                }
                TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, 0, null);
            }
            else
            {
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING | TrayNative.MF_DEFAULT,
                    SettingsCommand, "Настроить сценарии…");
                TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, 0, null);
            }

            if (_settings.IsConfigured)
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING, SettingsCommand, "Настройки…");
            TrayNative.AppendMenu(menu, TrayNative.MF_STRING, ExitCommand, "Выход");

            TrayNative.GetCursorPos(out TrayNative.POINT point);
            TrayNative.SetForegroundWindow(_window);
            uint command = TrayNative.TrackPopupMenu(menu,
                TrayNative.TPM_RIGHTBUTTON | TrayNative.TPM_RETURNCMD,
                point.X, point.Y, 0, _window, nint.Zero);
            TrayNative.PostMessage(_window, 0, 0, 0);
            _dispatcher.TryEnqueue(() => Execute(command));
        }
        finally
        {
            TrayNative.DestroyMenu(menu);
        }
    }

    private void Execute(uint command)
    {
        if (command >= ScenarioCommandBase &&
            command < ScenarioCommandBase + _settings.Current.Scenarios.Count)
        {
            int index = (int)(command - ScenarioCommandBase);
            _ = ApplyAsync(_settings.Current.Scenarios[index].Id);
            return;
        }
        switch (command)
        {
            case SwitchCommand: ApplyNext(); break;
            case SettingsCommand: ShowSettings(); break;
            case ExitCommand: App.Quit(); break;
        }
    }

    private void ApplyNext()
    {
        if (!_settings.IsConfigured)
        {
            ShowSettings();
            return;
        }
        _ = ApplyAsync(GetNextScenario().Id);
    }

    private ScenarioDefinition GetNextScenario()
    {
        int activeIndex = _settings.Current.Scenarios.FindIndex(scenario =>
            scenario.Id == _settings.Current.ActiveScenarioId);
        int nextIndex = activeIndex < 0 ? 0 : (activeIndex + 1) % _settings.Current.Scenarios.Count;
        return _settings.Current.Scenarios[nextIndex];
    }

    private int GetActiveNumber()
    {
        int index = _settings.Current.Scenarios.FindIndex(scenario =>
            scenario.Id == _settings.Current.ActiveScenarioId);
        return index is >= 0 and < 9 ? index + 1 : 0;
    }

    private async Task ApplyAsync(Guid scenarioId)
    {
        ApplyResult result = await _scenarios.ApplyAsync(scenarioId);
        ShowNotification(result.Message, result.Success);
        Refresh();
    }

    internal Task ApplyScenarioAsync(Guid scenarioId) => ApplyAsync(scenarioId);

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
            SettingsStore.Log(ex);
            ShowNotification("Не удалось открыть настройки. Подробности записаны в error.log.", false);
        }
    }

    internal void Refresh()
    {
        nint newIcon = TrayIconFactory.Create(GetActiveNumber());
        nint oldIcon = _icon;
        _icon = newIcon;
        _notifyData.hIcon = newIcon;
        _notifyData.szTip = BuildTooltip();
        _notifyData.uFlags = TrayNative.NIF_ICON | TrayNative.NIF_TIP;
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_MODIFY, ref _notifyData);
        if (oldIcon != nint.Zero) TrayNative.DestroyIcon(oldIcon);
    }

    private string BuildTooltip()
    {
        if (!_settings.IsConfigured)
            return "Room Switcher Tray\nТребуется настройка";
        ScenarioDefinition next = GetNextScenario();
        return $"Room Switcher Tray\nДвойной клик: {next.Name}";
    }

    private void ShowNotification(string message, bool success)
    {
        _notifyData.uFlags = TrayNative.NIF_INFO;
        _notifyData.szInfoTitle = "Room Switcher Tray";
        _notifyData.szInfo = message;
        _notifyData.dwInfoFlags = success ? TrayNative.NIIF_INFO : TrayNative.NIIF_ERROR;
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_MODIFY, ref _notifyData);
    }

    public void Dispose()
    {
        if (_window != nint.Zero)
        {
            TrayNative.Shell_NotifyIcon(TrayNative.NIM_DELETE, ref _notifyData);
            TrayNative.DestroyWindow(_window);
            _window = nint.Zero;
        }
        if (_icon != nint.Zero)
        {
            TrayNative.DestroyIcon(_icon);
            _icon = nint.Zero;
        }
    }
}
