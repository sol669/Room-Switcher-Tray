using RoomSwitcherTray.Core.Services;
using System.Runtime.InteropServices;
using System.Text;

namespace RoomSwitcherTray.Core;

public sealed class SettingsWindow : IDisposable
{
    private const string WindowClass = "sol669.RoomSwitcherTray.Core.Settings";
    private const int IdScenarioList = 101, IdCreate = 102, IdDelete = 103;
    private const int IdName = 201, IdMonitor1 = 211, IdAudio = 220;
    private const int IdRefresh = 301, IdSave = 302, IdSaveApply = 303, IdCancel = 304;
    private const uint WM_COMMAND = 0x0111, WM_CLOSE = 0x0010, WM_NCCREATE = 0x0081,
        WM_NCDESTROY = 0x0082, WM_SETFONT = 0x0030;
    private const uint CB_ADDSTRING = 0x0143, CB_GETCURSEL = 0x0147,
        CB_RESETCONTENT = 0x014B, CB_SETCURSEL = 0x014E;
    private const uint LB_ADDSTRING = 0x0180, LB_GETCURSEL = 0x0188,
        LB_RESETCONTENT = 0x0184, LB_SETCURSEL = 0x0186;
    private const int LBN_SELCHANGE = 1;
    private const int WS_OVERLAPPED = 0, WS_CAPTION = 0x00C00000,
        WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000,
        WS_VISIBLE = 0x10000000, WS_CHILD = 0x40000000, WS_TABSTOP = 0x00010000,
        WS_VSCROLL = 0x00200000, WS_BORDER = 0x00800000, ES_AUTOHSCROLL = 0x0080,
        CBS_DROPDOWNLIST = 0x0003, LBS_NOTIFY = 0x0001, BS_DEFPUSHBUTTON = 0x0001;
    private const int SW_SHOWNORMAL = 1, COLOR_WINDOW = 5, IDC_ARROW = 32512,
        DEFAULT_GUI_FONT = 17, IdYes = 6;
    private const uint MB_YESNO = 0x00000004, MB_ICONWARNING = 0x00000030;

    private static readonly WindowProcedure StaticWindowProcedure = WindowProc;
    private static readonly object RegistrationLock = new();
    private static bool _registered;

    private readonly SettingsStore _settings;
    private readonly TrayService _tray;
    private readonly List<ScenarioDefinition> _workingScenarios;
    private readonly nint[] _monitorCombos = new nint[4];
    private nint _window, _scenarioList, _name, _audio, _status, _refresh,
        _save, _saveApply, _delete;
    private GCHandle _selfHandle;
    private IReadOnlyList<DisplayDevice> _displays = [];
    private IReadOnlyList<AudioDevice> _audioDevices = [];
    private int _selectedIndex = -1;
    private bool _loading;
    private bool _devicesLoaded;

    public event EventHandler? Closed;

    public SettingsWindow(SettingsStore settings, TrayService tray)
    {
        _settings = settings;
        _tray = tray;
        _workingScenarios = settings.Current.Scenarios.Select(s => s.Clone()).ToList();
    }

    public void Activate()
    {
        if (_window == nint.Zero) CreateWindow();
        else { ShowWindow(_window, SW_SHOWNORMAL); SetForegroundWindow(_window); }
    }

    private void CreateWindow()
    {
        EnsureRegistered();
        _selfHandle = GCHandle.Alloc(this);
        const int width = 920, height = 660;
        int x = Math.Max(0, (GetSystemMetrics(0) - width) / 2);
        int y = Math.Max(0, (GetSystemMetrics(1) - height) / 2);
        _window = CreateWindowEx(0, WindowClass, "Room Switcher Tray — сценарии",
            WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_VISIBLE,
            x, y, width, height, nint.Zero, nint.Zero, GetModuleHandle(null),
            GCHandle.ToIntPtr(_selfHandle));
        if (_window == nint.Zero)
        {
            _selfHandle.Free();
            throw new InvalidOperationException($"Не удалось создать окно настроек: {Marshal.GetLastWin32Error()}");
        }
        CreateControls();
        _selectedIndex = _workingScenarios.Count > 0 ? 0 : -1;
        ReloadScenarioList();
        ShowSelectedScenario();
        _ = ReloadDevicesAsync();
        SetForegroundWindow(_window);
    }

    private static void EnsureRegistered()
    {
        lock (RegistrationLock)
        {
            if (_registered) return;
            var wc = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = StaticWindowProcedure,
                hInstance = GetModuleHandle(null), hCursor = LoadCursor(nint.Zero, (nint)IDC_ARROW),
                hbrBackground = (nint)(COLOR_WINDOW + 1), lpszClassName = WindowClass
            };
            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
                throw new InvalidOperationException($"Не удалось зарегистрировать окно: {Marshal.GetLastWin32Error()}");
            _registered = true;
        }
    }

    private void CreateControls()
    {
        nint font = GetStockObject(DEFAULT_GUI_FONT);
        AddStatic("Сценарии", 24, 20, 220, 26, font);
        AddControl("BUTTON", "+ Создать сценарий", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
            24, 52, 220, 34, IdCreate, font);
        _scenarioList = AddControl("LISTBOX", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | WS_BORDER | LBS_NOTIFY,
            24, 96, 220, 420, IdScenarioList, font);
        _delete = AddControl("BUTTON", "Удалить сценарий", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
            24, 526, 220, 34, IdDelete, font);

        AddStatic("Редактор сценария", 278, 20, 592, 26, font);
        AddStatic("Название", 278, 62, 130, 24, font);
        _name = AddControl("EDIT", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
            420, 58, 450, 28, IdName, font);
        for (int index = 0; index < 4; index++)
        {
            int y = 104 + index * 42;
            AddStatic($"Монитор {index + 1}", 278, y + 4, 130, 24, font);
            _monitorCombos[index] = AddControl("COMBOBOX", string.Empty,
                WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
                420, y, 450, 240, IdMonitor1 + index, font);
        }
        AddStatic("Аудиоустройство", 278, 278, 130, 24, font);
        _audio = AddControl("COMBOBOX", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            420, 274, 450, 240, IdAudio, font);
        _status = AddStatic(string.Empty, 278, 326, 592, 64, font);
        _refresh = AddControl("BUTTON", "Обновить список устройств",
            WS_CHILD | WS_VISIBLE | WS_TABSTOP, 278, 404, 240, 34, IdRefresh, font);
        AddControl("BUTTON", "Закрыть", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
            510, 526, 100, 38, IdCancel, font);
        _save = AddControl("BUTTON", "Сохранить", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
            620, 526, 100, 38, IdSave, font);
        _saveApply = AddControl("BUTTON", "Сохранить и применить",
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON,
            730, 526, 140, 38, IdSaveApply, font);
    }

    private nint AddStatic(string text, int x, int y, int width, int height, nint font) =>
        AddControl("STATIC", text, WS_CHILD | WS_VISIBLE, x, y, width, height, 0, font);

    private nint AddControl(string className, string text, int style,
        int x, int y, int width, int height, int id, nint font)
    {
        nint control = CreateWindowEx(0, className, text, style, x, y, width, height,
            _window, (nint)id, GetModuleHandle(null), nint.Zero);
        if (control != nint.Zero) SendMessage(control, WM_SETFONT, font, (nint)1);
        return control;
    }

    private void ReloadScenarioList()
    {
        _loading = true;
        SendMessage(_scenarioList, LB_RESETCONTENT, nint.Zero, nint.Zero);
        foreach (ScenarioDefinition scenario in _workingScenarios)
            SendMessageString(_scenarioList, LB_ADDSTRING, nint.Zero,
                string.IsNullOrWhiteSpace(scenario.Name) ? "Новый сценарий" : scenario.Name);
        SendMessage(_scenarioList, LB_SETCURSEL, (nint)_selectedIndex, nint.Zero);
        _loading = false;
    }

    private void ShowSelectedScenario()
    {
        bool exists = _selectedIndex >= 0 && _selectedIndex < _workingScenarios.Count;
        SetEditorEnabled(exists);
        if (!exists)
        {
            SetWindowText(_name, string.Empty);
            foreach (nint combo in _monitorCombos) SendMessage(combo, CB_RESETCONTENT, nint.Zero, nint.Zero);
            SendMessage(_audio, CB_RESETCONTENT, nint.Zero, nint.Zero);
            SetWindowText(_status, "Создайте первый сценарий.");
            return;
        }
        ScenarioDefinition scenario = _workingScenarios[_selectedIndex];
        SetWindowText(_name, scenario.Name);
        for (int index = 0; index < 4; index++)
            BindDisplayCombo(_monitorCombos[index],
                index < scenario.DisplayIds.Count ? scenario.DisplayIds[index] : null, index > 0);
        BindAudioCombo(scenario.AudioDeviceId, scenario.AudioDeviceContainerId);
    }

    private void CaptureEditorDraft()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _workingScenarios.Count) return;
        ScenarioDefinition scenario = _workingScenarios[_selectedIndex];
        scenario.Name = GetText(_name).Trim();
        if (!_devicesLoaded) return;
        scenario.DisplayIds = _monitorCombos.Select((combo, index) => SelectedDisplay(combo, index > 0)?.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToList();
        AudioDevice? audio = SelectedAudio();
        if (audio is not null)
        {
            scenario.AudioDeviceId = audio.Id;
            scenario.AudioDeviceContainerId = audio.ContainerId?.ToString("D") ?? string.Empty;
        }
    }

    private async Task ReloadDevicesAsync()
    {
        CaptureEditorDraft();
        SetBusy(true);
        SetWindowText(_status, "Поиск устройств…");
        try
        {
            _displays = await Task.Run(App.Displays.GetDisplays);
            _audioDevices = await App.Audio.GetVisibleRenderDevicesAsync(
                _displays, _workingScenarios.Cast<ScenarioDefinition?>().ToArray());
            _devicesLoaded = true;
            ShowSelectedScenario();
            SetWindowText(_status, _displays.Count == 0 || _audioDevices.Count == 0
                ? "Не удалось найти все необходимые устройства." : string.Empty);
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            SetWindowText(_status, $"Не удалось получить устройства: {ex.Message}");
        }
        finally { SetBusy(false); }
    }

    private void BindDisplayCombo(nint combo, string? selectedId, bool optional)
    {
        SendMessage(combo, CB_RESETCONTENT, nint.Zero, nint.Zero);
        int offset = optional ? 1 : 0, selected = optional ? 0 : -1;
        if (optional) SendMessageString(combo, CB_ADDSTRING, nint.Zero, "Нет");
        for (int index = 0; index < _displays.Count; index++)
        {
            DisplayDevice display = _displays[index];
            SendMessageString(combo, CB_ADDSTRING, nint.Zero, display.ToString());
            if (display.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) selected = index + offset;
        }
        if (selected < 0 && _displays.Count > 0) selected = 0;
        SendMessage(combo, CB_SETCURSEL, (nint)selected, nint.Zero);
    }

    private DisplayDevice? SelectedDisplay(nint combo, bool optional)
    {
        int index = (int)SendMessage(combo, CB_GETCURSEL, nint.Zero, nint.Zero) - (optional ? 1 : 0);
        return index >= 0 && index < _displays.Count ? _displays[index] : null;
    }

    private void BindAudioCombo(string? selectedId, string? selectedContainerId)
    {
        SendMessage(_audio, CB_RESETCONTENT, nint.Zero, nint.Zero);
        int selected = -1;
        bool hasContainer = Guid.TryParse(selectedContainerId, out Guid selectedContainer);
        for (int index = 0; index < _audioDevices.Count; index++)
        {
            AudioDevice device = _audioDevices[index];
            SendMessageString(_audio, CB_ADDSTRING, nint.Zero, device.ToString());
            if (device.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) selected = index;
            else if (selected < 0 && hasContainer && device.ContainerId == selectedContainer) selected = index;
        }
        if (selected < 0 && _audioDevices.Count > 0) selected = 0;
        SendMessage(_audio, CB_SETCURSEL, (nint)selected, nint.Zero);
    }

    private AudioDevice? SelectedAudio()
    {
        int index = (int)SendMessage(_audio, CB_GETCURSEL, nint.Zero, nint.Zero);
        return index >= 0 && index < _audioDevices.Count ? _audioDevices[index] : null;
    }

    private void CreateScenario()
    {
        CaptureEditorDraft();
        if (_selectedIndex >= 0 && !_workingScenarios[_selectedIndex].IsComplete)
        {
            SetWindowText(_status, "Сначала завершите текущий сценарий или удалите его.");
            return;
        }
        _workingScenarios.Add(new ScenarioDefinition());
        _selectedIndex = _workingScenarios.Count - 1;
        ReloadScenarioList();
        ShowSelectedScenario();
        SetWindowText(_status, "Настройте новый сценарий и нажмите «Сохранить».");
    }

    private void SelectScenario()
    {
        if (_loading) return;
        int index = (int)SendMessage(_scenarioList, LB_GETCURSEL, nint.Zero, nint.Zero);
        if (index == _selectedIndex) return;
        CaptureEditorDraft();
        _selectedIndex = index;
        ShowSelectedScenario();
        SetWindowText(_status, string.Empty);
    }

    private void DeleteScenario()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _workingScenarios.Count) return;
        ScenarioDefinition scenario = _workingScenarios[_selectedIndex];
        if (MessageBox(_window, $"Удалить сценарий «{scenario.Name}»?", "Room Switcher Tray",
            MB_YESNO | MB_ICONWARNING) != IdYes) return;
        _workingScenarios.RemoveAt(_selectedIndex);
        if (_settings.Current.ActiveScenarioId == scenario.Id) _settings.Current.ActiveScenarioId = null;
        _settings.Current.Scenarios.RemoveAll(item => item.Id == scenario.Id);
        _settings.Save();
        _tray.Refresh();
        _selectedIndex = _workingScenarios.Count == 0 ? -1 : Math.Min(_selectedIndex, _workingScenarios.Count - 1);
        ReloadScenarioList();
        ShowSelectedScenario();
        SetWindowText(_status, "Сценарий удалён.");
    }

    private bool SaveEditor()
    {
        CaptureEditorDraft();
        if (_selectedIndex < 0 || _selectedIndex >= _workingScenarios.Count)
        { SetWindowText(_status, "Создайте сценарий."); return false; }
        ScenarioDefinition current = _workingScenarios[_selectedIndex];
        if (string.IsNullOrWhiteSpace(current.Name))
        { SetWindowText(_status, "Введите название сценария."); return false; }
        if (current.DisplayIds.Count == 0)
        { SetWindowText(_status, "Выберите монитор 1."); return false; }
        if (current.DisplayIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != current.DisplayIds.Count)
        { SetWindowText(_status, "Мониторы в одном сценарии не должны повторяться."); return false; }
        if (string.IsNullOrWhiteSpace(current.AudioDeviceId))
        { SetWindowText(_status, "Выберите аудиоустройство."); return false; }
        if (_workingScenarios.Any(s => !s.IsComplete))
        { SetWindowText(_status, "Завершите настройку всех созданных сценариев."); return false; }
        PersistWorkingScenarios();
        ReloadScenarioList();
        SetWindowText(_status, "Сценарии сохранены.");
        return true;
    }

    private void PersistWorkingScenarios()
    {
        _settings.Current.Scenarios = _workingScenarios.Select(s => s.Clone()).ToList();
        if (_settings.Current.ActiveScenarioId.HasValue &&
            _settings.Current.Scenarios.All(s => s.Id != _settings.Current.ActiveScenarioId.Value))
            _settings.Current.ActiveScenarioId = null;
        _settings.Save();
        _tray.Refresh();
    }

    private async Task SaveAndApplyAsync()
    {
        if (!SaveEditor()) return;
        Guid id = _workingScenarios[_selectedIndex].Id;
        SetBusy(true);
        try { await _tray.ApplyScenarioAsync(id); }
        finally { SetBusy(false); }
    }

    private void SetEditorEnabled(bool enabled)
    {
        EnableWindow(_name, enabled);
        foreach (nint combo in _monitorCombos) EnableWindow(combo, enabled);
        EnableWindow(_audio, enabled); EnableWindow(_delete, enabled);
        EnableWindow(_save, enabled); EnableWindow(_saveApply, enabled);
    }

    private void SetBusy(bool busy)
    {
        EnableWindow(_refresh, !busy);
        EnableWindow(_save, !busy && _selectedIndex >= 0);
        EnableWindow(_saveApply, !busy && _selectedIndex >= 0);
    }

    private static string GetText(nint window)
    {
        int length = GetWindowTextLength(window);
        var value = new StringBuilder(length + 1);
        GetWindowText(window, value, value.Capacity);
        return value.ToString();
    }

    private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WM_COMMAND)
        {
            ulong command = wParam.ToUInt64();
            int id = (int)(command & 0xFFFF), notification = (int)((command >> 16) & 0xFFFF);
            if (id == IdScenarioList && notification == LBN_SELCHANGE) SelectScenario();
            else if (id == IdCreate) CreateScenario();
            else if (id == IdDelete) DeleteScenario();
            else if (id == IdRefresh) _ = ReloadDevicesAsync();
            else if (id == IdSave) SaveEditor();
            else if (id == IdSaveApply) _ = SaveAndApplyAsync();
            else if (id == IdCancel) DestroyWindow(window);
            return nint.Zero;
        }
        if (message == WM_CLOSE) { DestroyWindow(window); return nint.Zero; }
        if (message == WM_NCDESTROY)
        {
            _window = nint.Zero;
            if (_selfHandle.IsAllocated) _selfHandle.Free();
            Closed?.Invoke(this, EventArgs.Empty);
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WM_NCCREATE)
        {
            CREATESTRUCT create = Marshal.PtrToStructure<CREATESTRUCT>(lParam);
            SetWindowLongPtr(window, -21, create.lpCreateParams);
        }
        nint pointer = GetWindowLongPtr(window, -21);
        if (pointer != nint.Zero)
        {
            var handle = GCHandle.FromIntPtr(pointer);
            if (handle.Target is SettingsWindow settings) return settings.HandleMessage(window, message, wParam, lParam);
        }
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose()
    {
        if (_window != nint.Zero) DestroyWindow(_window);
        if (_selfHandle.IsAllocated) _selfHandle.Free();
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style; public WindowProcedure lpfnWndProc;
        public int cbClsExtra, cbWndExtra; public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName; public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREATESTRUCT
    {
        public nint lpCreateParams, hInstance, hMenu, hwndParent; public int cy, cx, y, x;
        public int style; public nint lpszName, lpszClass; public uint dwExStyle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WNDCLASSEX windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint LoadCursor(nint instance, nint cursorName);
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int objectId);
    [DllImport("user32.dll")] private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageW")] private static extern nint SendMessageString(nint window, uint message, nint wParam, string lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(nint window, string text);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint window, StringBuilder text, int maxCount);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(nint window);
    [DllImport("user32.dll")] private static extern bool EnableWindow(nint window, bool enable);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int MessageBox(nint window, string text, string caption, uint type);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
}
