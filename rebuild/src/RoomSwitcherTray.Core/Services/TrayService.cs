using RoomSwitcherTray.Core.Interop;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core.Services;

public sealed class TrayService : IDisposable
{
    private const uint TrayMessage = TrayNative.WM_APP + 1;
    private const int SwitchHotKeyId = 1;
    private const uint SwitchCommand = 1000;
    private const uint ScenarioCommandBase = 2000;
    private const uint SettingsCommand = 3000;
    private const uint ExitCommand = 3001;
    private const uint HdrCommandBase = 4000;
    private const uint MuteCommand = 5000;

    private readonly SettingsStore _settings;
    private readonly ScenarioService _scenarios;
    private readonly TrayNative.WindowProcedure _windowProcedure;
    private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcher;
    private readonly Dictionary<uint, ActiveDisplayStatus> _hdrCommands = [];
    private nint _window;
    private nint _icon;
    private TrayNative.NOTIFYICONDATA _notifyData;
    private WinUiSettingsWindow? _settingsWindow;
    private bool _hotKeyRegistered;

    public TrayService(SettingsStore settings, ScenarioService scenarios)
    {
        _settings = settings;
        _scenarios = scenarios;
        _windowProcedure = WindowProc;
        _dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
    }

    public void Initialize()
    {
        const string className = "sol669.RoomSwitcher.Core.TrayWindow";
        nint instance = TrayNative.GetModuleHandle(null);
        var windowClass = new TrayNative.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<TrayNative.WNDCLASSEX>(),
            lpfnWndProc = _windowProcedure,
            hInstance = instance,
            lpszClassName = className
        };
        TrayNative.RegisterClassEx(ref windowClass);
        _window = TrayNative.CreateWindowEx(0, className, "RoomSwitcher", 0,
            0, 0, 0, 0, nint.Zero, nint.Zero, instance, nint.Zero);
        TrayNative.WTSRegisterSessionNotification(_window, TrayNative.NOTIFY_FOR_THIS_SESSION);

        _icon = TrayIconFactory.Create();
        _notifyData = new TrayNative.NOTIFYICONDATA
        {
            cbSize = (uint)Marshal.SizeOf<TrayNative.NOTIFYICONDATA>(), hWnd = _window, uID = 1,
            uFlags = TrayNative.NIF_MESSAGE | TrayNative.NIF_ICON | TrayNative.NIF_TIP,
            uCallbackMessage = TrayMessage, hIcon = _icon, szTip = BuildTooltip(),
            szInfo = string.Empty, szInfoTitle = string.Empty
        };
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_ADD, ref _notifyData);
        HotKeyDefinition hotKey = _settings.Current.SwitchScenarioHotKey ?? HotKeyDefinition.Default;
        _hotKeyRegistered = TrayNative.RegisterHotKey(_window, SwitchHotKeyId,
            hotKey.Modifiers | TrayNative.MOD_NOREPEAT, hotKey.VirtualKey);
        if (!_hotKeyRegistered)
            ShowNotification($"Не удалось назначить {FormatHotKey(hotKey)}: комбинация уже занята.", false);
    }

    private nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (message == TrayMessage)
            {
                if (unchecked((uint)lParam.ToInt64()) == TrayNative.WM_RBUTTONUP) ShowMenu();
                return nint.Zero;
            }
            if (message == TrayNative.WM_HOTKEY && (int)wParam == SwitchHotKeyId)
            {
                if (!IsRemoteSession) _dispatcher.TryEnqueue(ApplyNext);
                return nint.Zero;
            }
            if (message == TrayNative.WM_WTSSESSION_CHANGE)
            {
                _dispatcher.TryEnqueue(Refresh);
                return nint.Zero;
            }
        }
        catch (Exception ex) { SettingsStore.Log(ex); }
        return TrayNative.DefWindowProc(window, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        NativeTheme.Apply(_window, _settings.Current.Theme);
        nint menu = TrayNative.CreatePopupMenu();
        try
        {
            _hdrCommands.Clear();
            if (IsRemoteSession)
            {
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING | TrayNative.MF_DISABLED, 0, UiText.Get(_settings.Current, "Remote"));
                TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, 0, null);
                BuildStatusSection(menu, remoteSession: true);
                TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, 0, null);
            }
            else if (_settings.IsConfigured)
            {
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING, SwitchCommand,
                    $"{UiText.Get(_settings.Current, "Next")}\t{FormatHotKey(_settings.Current.SwitchScenarioHotKey)}");
                TrayNative.SetMenuDefaultItem(menu, SwitchCommand, 0);
                foreach ((ScenarioDefinition scenario, int index) in _settings.Current.Scenarios.Select((item, index) => (item, index)))
                    TrayNative.AppendMenu(menu, TrayNative.MF_STRING |
                        (_settings.Current.ActiveScenarioId == scenario.Id ? TrayNative.MF_CHECKED : 0),
                        ScenarioCommandBase + (uint)index, scenario.Name);

                TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, 0, null);
                BuildStatusSection(menu, remoteSession: false);
                TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, 0, null);
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING, SettingsCommand, UiText.Get(_settings.Current, "Settings"));
            }
            else
            {
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING,
                    SettingsCommand, UiText.Get(_settings.Current, "Configure"));
                TrayNative.SetMenuDefaultItem(menu, SettingsCommand, 0);
                TrayNative.AppendMenu(menu, TrayNative.MF_SEPARATOR, 0, null);
            }
            TrayNative.AppendMenu(menu, TrayNative.MF_STRING, ExitCommand, UiText.Get(_settings.Current, "Exit"));

            TrayNative.GetCursorPos(out TrayNative.POINT point);
            TrayNative.SetForegroundWindow(_window);
            uint command = TrayNative.TrackPopupMenu(menu, TrayNative.TPM_RIGHTBUTTON | TrayNative.TPM_RETURNCMD,
                point.X, point.Y, 0, _window, nint.Zero);
            TrayNative.PostMessage(_window, 0, 0, 0);
            _dispatcher.TryEnqueue(() => Execute(command));
        }
        finally { TrayNative.DestroyMenu(menu); }
    }

    private void BuildStatusSection(nint menu, bool remoteSession)
    {
        bool added = false;
        try
        {
            IReadOnlyList<ActiveDisplayStatus> displays = remoteSession
                ? App.Displays.GetActiveDisplayStatuses()
                : GetActiveScenario() is ScenarioDefinition activeScenario
                    ? App.Displays.GetActiveDisplayStatuses(activeScenario.DisplayIds) : [];
            foreach (ActiveDisplayStatus rawDisplay in displays)
            {
                ActiveDisplayStatus display = rawDisplay;
                string resolution = display.Width > 0 && display.Height > 0
                    ? $"{display.Width} × {display.Height}" : UiText.Get(_settings.Current, "UnknownResolution");
                string displayName = remoteSession ? display.Name : DeviceAliasService.NameFor(_settings.Current, display.Id, display.Name);
                string text = $"{displayName}\t{resolution} · {(remoteSession ? "SDR" : display.HdrEnabled ? "HDR" : "SDR")}";
                if (!remoteSession && display.HdrSupported)
                {
                    uint command = HdrCommandBase + (uint)_hdrCommands.Count;
                    _hdrCommands[command] = display;
                    nint submenu = TrayNative.CreatePopupMenu();
                    TrayNative.AppendMenu(submenu, TrayNative.MF_STRING, command,
                        display.HdrEnabled ? UiText.Get(_settings.Current, "DisableHdr") : UiText.Get(_settings.Current, "EnableHdr"));
                    TrayNative.AppendMenu(menu, TrayNative.MF_STRING | TrayNative.MF_POPUP, (nuint)submenu, text);
                }
                else TrayNative.AppendMenu(menu, TrayNative.MF_STRING | TrayNative.MF_DISABLED, 0, text);
                added = true;
            }

            AudioEndpointStatus? audio = App.Audio.GetDefaultEndpointStatus();
            if (audio is not null)
            {
                string level = audio.IsMuted ? $"{UiText.Get(_settings.Current, "Muted")} · {audio.VolumePercent}%" : $"{audio.VolumePercent}%";
                nint submenu = TrayNative.CreatePopupMenu();
                TrayNative.AppendMenu(submenu, TrayNative.MF_STRING |
                    (audio.IsMuted ? TrayNative.MF_CHECKED : 0), MuteCommand, UiText.Get(_settings.Current, "Mute"));
                string? audioId = App.Audio.GetDefaultEndpointId();
                string audioName = remoteSession ? audio.Name : DeviceAliasService.NameFor(_settings.Current, audioId ?? string.Empty, audio.Name);
                TrayNative.AppendMenu(menu, TrayNative.MF_STRING | TrayNative.MF_POPUP,
                    (nuint)submenu, $"{audioName}\t{level}");
                added = true;
            }
        }
        catch (Exception ex) { SettingsStore.Log(ex); }
        if (!added)
            TrayNative.AppendMenu(menu, TrayNative.MF_STRING | TrayNative.MF_DISABLED,
                0, UiText.Get(_settings.Current, "NoDevices"));
    }

    private void Execute(uint command)
    {
        if (command >= ScenarioCommandBase && command < ScenarioCommandBase + _settings.Current.Scenarios.Count)
        { _ = ApplyAsync(_settings.Current.Scenarios[(int)(command - ScenarioCommandBase)].Id); return; }
        if (_hdrCommands.TryGetValue(command, out ActiveDisplayStatus? display)) { _ = ToggleHdrAsync(display); return; }
        switch (command)
        {
            case SwitchCommand: ApplyNext(); break;
            case MuteCommand: ToggleMute(); break;
            case SettingsCommand: ShowSettings(); break;
            case ExitCommand: App.Quit(); break;
        }
    }

    private async Task ToggleHdrAsync(ActiveDisplayStatus display)
    {
        bool targetState = !display.HdrEnabled;
        try
        {
            await Task.Run(() => App.Displays.SetHdr(display.Id, targetState));
            ShowNotification($"HDR для «{display.Name}» {(display.HdrEnabled ? "выключен" : "включён")}.", true);
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            ShowNotification($"Не удалось изменить HDR: {ex.Message}", false);
        }
    }


    private void ToggleMute()
    {
        try
        {
            AudioEndpointStatus? current = App.Audio.GetDefaultEndpointStatus();
            if (current is null) throw new InvalidOperationException("Активное аудиоустройство не найдено.");
            App.Audio.SetDefaultEndpointMuted(!current.IsMuted);
            ShowNotification(current.IsMuted ? "Звук включён." : "Звук выключен.", true);
        }
        catch (Exception ex) { SettingsStore.Log(ex); ShowNotification($"Не удалось изменить звук: {ex.Message}", false); }
    }

    private void ApplyNext()
    {
        if (IsRemoteSession) return;
        if (!_settings.IsConfigured) { ShowSettings(); return; }
        _ = ApplyAsync(GetNextScenario().Id);
    }

    private ScenarioDefinition GetNextScenario()
    {
        int activeIndex = _settings.Current.Scenarios.FindIndex(s => s.Id == _settings.Current.ActiveScenarioId);
        return _settings.Current.Scenarios[activeIndex < 0 ? 0 : (activeIndex + 1) % _settings.Current.Scenarios.Count];
    }

    private ScenarioDefinition? GetActiveScenario() => _settings.Current.ActiveScenarioId is Guid id
        ? _settings.Current.Scenarios.FirstOrDefault(scenario => scenario.Id == id) : null;

    private async Task ApplyAsync(Guid scenarioId)
    {
        ApplyResult result = await _scenarios.ApplyAsync(scenarioId);
        ShowNotification(result.Message, result.Success);
        Refresh();
    }

    internal Task ApplyScenarioAsync(Guid scenarioId) => ApplyAsync(scenarioId);

    internal bool TryUpdateHotKey(HotKeyDefinition hotKey, out string error)
    {
        if (hotKey.Modifiers == 0 || hotKey.VirtualKey == 0)
        { error = "Выберите сочетание с Ctrl, Alt, Shift или Win и обычной клавишей."; return false; }
        if (_hotKeyRegistered) TrayNative.UnregisterHotKey(_window, SwitchHotKeyId);
        _hotKeyRegistered = TrayNative.RegisterHotKey(_window, SwitchHotKeyId, hotKey.Modifiers | TrayNative.MOD_NOREPEAT, hotKey.VirtualKey);
        if (_hotKeyRegistered) { error = string.Empty; return true; }
        HotKeyDefinition previous = _settings.Current.SwitchScenarioHotKey ?? HotKeyDefinition.Default;
        _hotKeyRegistered = TrayNative.RegisterHotKey(_window, SwitchHotKeyId, previous.Modifiers | TrayNative.MOD_NOREPEAT, previous.VirtualKey);
        error = $"{FormatHotKey(hotKey)} уже занято другой программой.";
        return false;
    }

    public void ShowSettings()
    {
        if (_settingsWindow is not null) { _settingsWindow.Activate(); return; }
        try
        {
            _settingsWindow = new WinUiSettingsWindow(_settings, this);
            _settingsWindow.ClosedByUser += (_, _) => _settingsWindow = null;
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
        _notifyData.szTip = BuildTooltip();
        _notifyData.uFlags = TrayNative.NIF_TIP;
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_MODIFY, ref _notifyData);
    }

    private string BuildTooltip() => IsRemoteSession ? UiText.Get(_settings.Current, "Remote") : GetActiveScenario()?.Name ?? "RoomSwitcher";

    private static bool IsRemoteSession => TrayNative.GetSystemMetrics(TrayNative.SM_REMOTESESSION) != 0;

    internal static string FormatHotKey(HotKeyDefinition? hotKey)
    {
        hotKey ??= HotKeyDefinition.Default;
        var parts = new List<string>();
        if ((hotKey.Modifiers & HotKeyDefinition.Control) != 0) parts.Add("Ctrl");
        if ((hotKey.Modifiers & HotKeyDefinition.Alt) != 0) parts.Add("Alt");
        if ((hotKey.Modifiers & HotKeyDefinition.Shift) != 0) parts.Add("Shift");
        if ((hotKey.Modifiers & HotKeyDefinition.Win) != 0) parts.Add("Win");
        parts.Add(FormatVirtualKey(hotKey.VirtualKey));
        return string.Join(" + ", parts);
    }

    private static string FormatVirtualKey(uint key) => key switch
    {
        TrayNative.VK_SPACE => "Space",
        >= 0x70 and <= 0x87 => $"F{key - 0x6F}",
        0x1B => "Esc",
        0x09 => "Tab",
        0x0D => "Enter",
        0x2E => "Delete",
        _ when key is >= 0x30 and <= 0x5A => ((char)key).ToString().ToUpperInvariant(),
        _ => $"Клавиша {key}"
    };

    private void ShowNotification(string message, bool success)
    {
        _notifyData.uFlags = TrayNative.NIF_INFO;
        _notifyData.szInfoTitle = "RoomSwitcher";
        _notifyData.szInfo = message;
        _notifyData.dwInfoFlags = success ? TrayNative.NIIF_INFO : TrayNative.NIIF_ERROR;
        TrayNative.Shell_NotifyIcon(TrayNative.NIM_MODIFY, ref _notifyData);
    }

    public void Dispose()
    {
        if (_window != nint.Zero)
        {
            if (_hotKeyRegistered) TrayNative.UnregisterHotKey(_window, SwitchHotKeyId);
            TrayNative.WTSUnRegisterSessionNotification(_window);
            TrayNative.Shell_NotifyIcon(TrayNative.NIM_DELETE, ref _notifyData);
            TrayNative.DestroyWindow(_window);
            _window = nint.Zero;
        }
        if (_icon != nint.Zero) { TrayNative.DestroyIcon(_icon); _icon = nint.Zero; }
    }
}
