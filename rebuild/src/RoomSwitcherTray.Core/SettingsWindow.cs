using RoomSwitcherTray.Core.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace RoomSwitcherTray.Core;

/// <summary>Functional native editor until the final WinUI shell is introduced.</summary>
public sealed class SettingsWindow : IDisposable
{
    private const string WindowClass = "sol669.RoomSwitcher.Core.Settings";
    private const int IdNavGeneral = 101, IdNavScenarios = 102;
    private const int IdStartWithWindows = 111, IdStartupMode = 112, IdStartupScenario = 113, IdSaveGeneral = 114;
    private const int IdScenarioList = 201, IdCreate = 202, IdDelete = 203;
    private const int IdName = 301, IdMonitor1 = 311, IdAudio = 320, IdVolume = 321;
    private const int IdRefresh = 401, IdSave = 402, IdSaveApply = 403, IdSaveOpenDisplay = 404, IdClose = 405;
    private const uint WM_COMMAND = 0x0111, WM_CLOSE = 0x0010, WM_NCCREATE = 0x0081, WM_NCDESTROY = 0x0082, WM_SETFONT = 0x0030;
    private const uint CB_ADDSTRING = 0x0143, CB_GETCURSEL = 0x0147, CB_RESETCONTENT = 0x014B, CB_SETCURSEL = 0x014E;
    private const uint LB_ADDSTRING = 0x0180, LB_GETCURSEL = 0x0188, LB_RESETCONTENT = 0x0184, LB_SETCURSEL = 0x0186;
    private const uint BM_GETCHECK = 0x00F0, BM_SETCHECK = 0x00F1;
    private const int LBN_SELCHANGE = 1, CBN_SELCHANGE = 1, BST_CHECKED = 1;
    private const int WS_OVERLAPPED = 0, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_MINIMIZEBOX = 0x00020000;
    private const int WS_VISIBLE = 0x10000000, WS_CHILD = 0x40000000, WS_TABSTOP = 0x00010000, WS_VSCROLL = 0x00200000, WS_BORDER = 0x00800000;
    private const int ES_AUTOHSCROLL = 0x0080, CBS_DROPDOWNLIST = 0x0003, LBS_NOTIFY = 0x0001, BS_DEFPUSHBUTTON = 0x0001, BS_AUTOCHECKBOX = 0x0003;
    private const int SW_HIDE = 0, SW_SHOWNORMAL = 1, COLOR_WINDOW = 5, IDC_ARROW = 32512, DEFAULT_GUI_FONT = 17, IdYes = 6;
    private const uint MB_YESNO = 0x00000004, MB_ICONWARNING = 0x00000030;

    private static readonly WindowProcedure StaticWindowProcedure = WindowProc;
    private static readonly object RegistrationLock = new();
    private static bool _registered;
    private static readonly int?[] VolumeChoices = [null, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100];

    private readonly SettingsStore _settings;
    private readonly TrayService _tray;
    private readonly List<ScenarioDefinition> _workingScenarios;
    private readonly nint[] _monitorCombos = new nint[4];
    private readonly List<DisplayDevice>[] _displayOptions = [[], [], [], []];
    private readonly List<nint> _generalControls = [];
    private readonly List<nint> _scenarioControls = [];
    private readonly List<Guid> _startupScenarioIds = [];
    private nint _window, _scenarioList, _name, _audio, _volume, _status, _refresh, _save, _saveApply, _saveOpenDisplay, _delete;
    private nint _startWithWindows, _startupMode, _startupScenario;
    private GCHandle _selfHandle;
    private IReadOnlyList<DisplayDevice> _displays = [];
    private IReadOnlyList<AudioDevice> _audioDevices = [];
    private int _selectedIndex = -1;
    private bool _loading, _devicesLoaded;
    private Page _page;

    public event EventHandler? Closed;
    private enum Page { None, General, Scenarios }

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
        const int width = 1040, height = 690;
        int x = Math.Max(0, (GetSystemMetrics(0) - width) / 2), y = Math.Max(0, (GetSystemMetrics(1) - height) / 2);
        _window = CreateWindowEx(0, WindowClass, "RoomSwitcher — настройки", WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX | WS_VISIBLE,
            x, y, width, height, nint.Zero, nint.Zero, GetModuleHandle(null), GCHandle.ToIntPtr(_selfHandle));
        if (_window == nint.Zero) { _selfHandle.Free(); throw new InvalidOperationException($"Не удалось создать окно настроек: {Marshal.GetLastWin32Error()}"); }

        CreateControls();
        _selectedIndex = _workingScenarios.Count > 0 ? 0 : -1;
        ReloadScenarioList();
        SetPage(Page.General);
        _ = ReloadDevicesAsync();
        SetForegroundWindow(_window);
    }

    private static void EnsureRegistered()
    {
        lock (RegistrationLock)
        {
            if (_registered) return;
            var wc = new WNDCLASSEX { cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(), lpfnWndProc = StaticWindowProcedure,
                hInstance = GetModuleHandle(null), hCursor = LoadCursor(nint.Zero, (nint)IDC_ARROW), hbrBackground = (nint)(COLOR_WINDOW + 1), lpszClassName = WindowClass };
            ushort atom = RegisterClassEx(ref wc);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410) throw new InvalidOperationException($"Не удалось зарегистрировать окно: {Marshal.GetLastWin32Error()}");
            _registered = true;
        }
    }

    private void CreateControls()
    {
        nint font = GetStockObject(DEFAULT_GUI_FONT);
        _page = Page.None;
        AddStatic("RoomSwitcher", 22, 22, 165, 25, font);
        AddControl("BUTTON", "Основные", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 22, 58, 165, 34, IdNavGeneral, font);
        AddControl("BUTTON", "Сценарии", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 22, 100, 165, 34, IdNavScenarios, font);

        _page = Page.General;
        AddStatic("Основные настройки", 220, 22, 760, 26, font);
        AddStatic("Запуск", 220, 68, 180, 22, font);
        _startWithWindows = AddControl("BUTTON", "Запускать RoomSwitcher при входе в Windows", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_AUTOCHECKBOX,
            220, 98, 420, 28, IdStartWithWindows, font);
        AddStatic("При запуске RoomSwitcher", 220, 154, 220, 22, font);
        _startupMode = AddControl("COMBOBOX", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            450, 150, 430, 180, IdStartupMode, font);
        AddStatic("Сценарий", 220, 200, 220, 22, font);
        _startupScenario = AddControl("COMBOBOX", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            450, 196, 430, 180, IdStartupScenario, font);
        AddStatic("По умолчанию RoomSwitcher не меняет текущую конфигурацию экранов.\r\nВыбери другой режим, если при запуске нужно применить сценарий автоматически.",
            220, 248, 660, 54, font);
        AddControl("BUTTON", "Сохранить основные настройки", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON,
            670, 572, 210, 38, IdSaveGeneral, font);

        _page = Page.Scenarios;
        AddStatic("Сценарии", 220, 22, 210, 26, font);
        AddControl("BUTTON", "+ Создать сценарий", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 220, 52, 210, 34, IdCreate, font);
        _scenarioList = AddControl("LISTBOX", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | WS_BORDER | LBS_NOTIFY,
            220, 96, 210, 420, IdScenarioList, font);
        _delete = AddControl("BUTTON", "Удалить сценарий", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 220, 526, 210, 34, IdDelete, font);

        AddStatic("Редактор сценария", 465, 22, 515, 26, font);
        AddStatic("Название", 465, 62, 130, 24, font);
        _name = AddControl("EDIT", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL, 600, 58, 380, 28, IdName, font);
        for (int index = 0; index < 4; index++)
        {
            int y = 104 + index * 42;
            AddStatic($"Монитор {index + 1}", 465, y + 4, 130, 24, font);
            _monitorCombos[index] = AddControl("COMBOBOX", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
                600, y, 380, 240, IdMonitor1 + index, font);
        }
        AddStatic("Аудиоустройство", 465, 278, 130, 24, font);
        _audio = AddControl("COMBOBOX", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            600, 274, 380, 240, IdAudio, font);
        AddStatic("Громкость", 465, 322, 130, 24, font);
        _volume = AddControl("COMBOBOX", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            600, 318, 380, 240, IdVolume, font);
        _status = AddStatic(string.Empty, 465, 366, 515, 48, font);
        _refresh = AddControl("BUTTON", "Обновить список устройств", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 465, 430, 240, 34, IdRefresh, font);
        _save = AddControl("BUTTON", "Сохранить", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 580, 572, 115, 38, IdSave, font);
        _saveOpenDisplay = AddControl("BUTTON", "Сохранить и настроить экраны", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 705, 572, 180, 38, IdSaveOpenDisplay, font);
        _saveApply = AddControl("BUTTON", "Применить", WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON, 895, 572, 85, 38, IdSaveApply, font);

        _page = Page.None;
        AddControl("BUTTON", "Закрыть", WS_CHILD | WS_VISIBLE | WS_TABSTOP, 875, 620, 105, 32, IdClose, font);
    }

    private nint AddStatic(string text, int x, int y, int width, int height, nint font) => AddControl("STATIC", text, WS_CHILD | WS_VISIBLE, x, y, width, height, 0, font);

    private nint AddControl(string className, string text, int style, int x, int y, int width, int height, int id, nint font)
    {
        nint control = CreateWindowEx(0, className, text, style, x, y, width, height, _window, (nint)id, GetModuleHandle(null), nint.Zero);
        if (control != nint.Zero) SendMessage(control, WM_SETFONT, font, (nint)1);
        if (_page == Page.General) _generalControls.Add(control);
        else if (_page == Page.Scenarios) _scenarioControls.Add(control);
        return control;
    }

    private void SetPage(Page page)
    {
        if (_page == Page.Scenarios && page != Page.Scenarios) CaptureEditorDraft();
        _page = page;
        foreach (nint control in _generalControls) ShowWindow(control, page == Page.General ? SW_SHOWNORMAL : SW_HIDE);
        foreach (nint control in _scenarioControls) ShowWindow(control, page == Page.Scenarios ? SW_SHOWNORMAL : SW_HIDE);
        if (page == Page.General) LoadGeneralSettings();
        else ShowSelectedScenario();
    }

    private void LoadGeneralSettings()
    {
        SendMessage(_startWithWindows, BM_SETCHECK, (nint)(StartupService.IsEnabled() ? BST_CHECKED : 0), nint.Zero);
        SendMessage(_startupMode, CB_RESETCONTENT, nint.Zero, nint.Zero);
        SendMessageString(_startupMode, CB_ADDSTRING, nint.Zero, "Не менять текущую конфигурацию");
        SendMessageString(_startupMode, CB_ADDSTRING, nint.Zero, "Восстановить последний сценарий");
        SendMessageString(_startupMode, CB_ADDSTRING, nint.Zero, "Всегда включать выбранный сценарий");
        SendMessage(_startupMode, CB_SETCURSEL, (nint)_settings.Current.StartupScenarioMode, nint.Zero);
        _startupScenarioIds.Clear();
        SendMessage(_startupScenario, CB_RESETCONTENT, nint.Zero, nint.Zero);
        foreach (ScenarioDefinition scenario in _workingScenarios.Where(scenario => scenario.IsComplete))
        {
            _startupScenarioIds.Add(scenario.Id);
            SendMessageString(_startupScenario, CB_ADDSTRING, nint.Zero, scenario.Name);
        }
        int selected = _startupScenarioIds.FindIndex(id => id == _settings.Current.StartupScenarioId);
        SendMessage(_startupScenario, CB_SETCURSEL, (nint)(selected >= 0 ? selected : 0), nint.Zero);
        UpdateStartupScenarioEnabled();
    }

    private void UpdateStartupScenarioEnabled()
    {
        int mode = (int)SendMessage(_startupMode, CB_GETCURSEL, nint.Zero, nint.Zero);
        EnableWindow(_startupScenario, mode == (int)StartupScenarioMode.AlwaysUseScenario && _startupScenarioIds.Count > 0);
    }

    private void SaveGeneralSettings()
    {
        int modeIndex = (int)SendMessage(_startupMode, CB_GETCURSEL, nint.Zero, nint.Zero);
        StartupScenarioMode mode = Enum.IsDefined(typeof(StartupScenarioMode), modeIndex)
            ? (StartupScenarioMode)modeIndex : StartupScenarioMode.KeepCurrentConfiguration;
        int scenarioIndex = (int)SendMessage(_startupScenario, CB_GETCURSEL, nint.Zero, nint.Zero);
        if (mode == StartupScenarioMode.AlwaysUseScenario && (scenarioIndex < 0 || scenarioIndex >= _startupScenarioIds.Count))
        { MessageBox(_window, "Сначала создай и сохрани хотя бы один сценарий.", "RoomSwitcher", MB_ICONWARNING); return; }
        try
        {
            bool startWithWindows = (int)SendMessage(_startWithWindows, BM_GETCHECK, nint.Zero, nint.Zero) == BST_CHECKED;
            StartupService.SetEnabled(startWithWindows);
            _settings.Current.StartWithWindows = startWithWindows;
            _settings.Current.StartupScenarioMode = mode;
            _settings.Current.StartupScenarioId = mode == StartupScenarioMode.AlwaysUseScenario ? _startupScenarioIds[scenarioIndex] : null;
            _settings.Save();
            MessageBox(_window, "Основные настройки сохранены.", "RoomSwitcher", 0);
        }
        catch (Exception ex) { SettingsStore.Log(ex); MessageBox(_window, ex.Message, "RoomSwitcher", MB_ICONWARNING); }
    }

    private void ReloadScenarioList()
    {
        if (_scenarioList == nint.Zero) return;
        _loading = true;
        SendMessage(_scenarioList, LB_RESETCONTENT, nint.Zero, nint.Zero);
        foreach (ScenarioDefinition scenario in _workingScenarios)
            SendMessageString(_scenarioList, LB_ADDSTRING, nint.Zero, string.IsNullOrWhiteSpace(scenario.Name) ? "Новый сценарий" : scenario.Name);
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
            SendMessage(_audio, CB_RESETCONTENT, nint.Zero, nint.Zero); SendMessage(_volume, CB_RESETCONTENT, nint.Zero, nint.Zero);
            SetWindowText(_status, "Создайте первый сценарий.");
            return;
        }
        _loading = true;
        ScenarioDefinition scenario = _workingScenarios[_selectedIndex];
        SetWindowText(_name, scenario.Name);
        for (int index = 0; index < 4; index++)
            BindDisplayCombo(index, index < scenario.DisplayIds.Count ? scenario.DisplayIds[index] : null, index > 0);
        BindAudioCombo(scenario.AudioDeviceId, scenario.AudioDeviceContainerId);
        BindVolumeCombo(scenario.VolumePercent);
        _loading = false;
    }

    private void CaptureEditorDraft()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _workingScenarios.Count) return;
        ScenarioDefinition scenario = _workingScenarios[_selectedIndex];
        scenario.Name = GetText(_name).Trim();
        if (!_devicesLoaded) return;
        scenario.DisplayIds = _monitorCombos.Select((combo, index) => SelectedDisplay(index, combo, index > 0)?.Id).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToList();
        AudioDevice? audio = SelectedAudio();
        if (audio is not null) { scenario.AudioDeviceId = audio.Id; scenario.AudioDeviceContainerId = audio.ContainerId?.ToString("D") ?? string.Empty; }
        scenario.VolumePercent = SelectedVolume();
    }

    private async Task ReloadDevicesAsync()
    {
        if (_page == Page.Scenarios) CaptureEditorDraft();
        SetBusy(true); SetWindowText(_status, "Поиск устройств…");
        try
        {
            _displays = await Task.Run(App.Displays.GetDisplays);
            _audioDevices = await App.Audio.GetVisibleRenderDevicesAsync(_displays, _workingScenarios.Cast<ScenarioDefinition?>().ToArray());
            _devicesLoaded = true;
            if (_page == Page.Scenarios) ShowSelectedScenario();
            SetWindowText(_status, _displays.Count == 0 || _audioDevices.Count == 0 ? "Не удалось найти все необходимые устройства." : string.Empty);
        }
        catch (Exception ex) { SettingsStore.Log(ex); SetWindowText(_status, $"Не удалось получить устройства: {ex.Message}"); }
        finally { SetBusy(false); }
    }

    private void BindDisplayCombo(int fieldIndex, string? selectedId, bool optional)
    {
        nint combo = _monitorCombos[fieldIndex];
        SendMessage(combo, CB_RESETCONTENT, nint.Zero, nint.Zero);
        ScenarioDefinition scenario = _workingScenarios[_selectedIndex];
        var usedElsewhere = scenario.DisplayIds.Where((id, index) => index != fieldIndex).ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<DisplayDevice> options = _displays.Where(display => display.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase) || !usedElsewhere.Contains(display.Id)).ToList();
        _displayOptions[fieldIndex] = options;
        int offset = optional ? 1 : 0, selected = optional ? 0 : -1;
        if (optional) SendMessageString(combo, CB_ADDSTRING, nint.Zero, "Нет");
        for (int index = 0; index < options.Count; index++)
        {
            SendMessageString(combo, CB_ADDSTRING, nint.Zero, options[index].ToString());
            if (options[index].Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) selected = index + offset;
        }
        if (selected < 0 && options.Count > 0) selected = 0;
        SendMessage(combo, CB_SETCURSEL, (nint)selected, nint.Zero);
    }

    private DisplayDevice? SelectedDisplay(int fieldIndex, nint combo, bool optional)
    {
        int index = (int)SendMessage(combo, CB_GETCURSEL, nint.Zero, nint.Zero) - (optional ? 1 : 0);
        return index >= 0 && index < _displayOptions[fieldIndex].Count ? _displayOptions[fieldIndex][index] : null;
    }

    private void BindAudioCombo(string? selectedId, string? selectedContainerId)
    {
        SendMessage(_audio, CB_RESETCONTENT, nint.Zero, nint.Zero); int selected = -1;
        bool hasContainer = Guid.TryParse(selectedContainerId, out Guid selectedContainer);
        for (int index = 0; index < _audioDevices.Count; index++)
        {
            AudioDevice device = _audioDevices[index]; SendMessageString(_audio, CB_ADDSTRING, nint.Zero, device.ToString());
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

    private void BindVolumeCombo(int? value)
    {
        SendMessage(_volume, CB_RESETCONTENT, nint.Zero, nint.Zero);
        for (int index = 0; index < VolumeChoices.Length; index++)
        {
            string label = VolumeChoices[index] switch { null => "Не менять", 0 => "0% — без звука", int percent => $"{percent}%" };
            SendMessageString(_volume, CB_ADDSTRING, nint.Zero, label);
        }
        int selected = Array.IndexOf(VolumeChoices, value); if (selected < 0) selected = 0;
        SendMessage(_volume, CB_SETCURSEL, (nint)selected, nint.Zero);
    }

    private int? SelectedVolume()
    {
        int index = (int)SendMessage(_volume, CB_GETCURSEL, nint.Zero, nint.Zero);
        return index >= 0 && index < VolumeChoices.Length ? VolumeChoices[index] : null;
    }

    private void CreateScenario()
    {
        CaptureEditorDraft();
        if (_selectedIndex >= 0 && !_workingScenarios[_selectedIndex].IsComplete) { SetWindowText(_status, "Сначала завершите текущий сценарий или удалите его."); return; }
        _workingScenarios.Add(new ScenarioDefinition()); _selectedIndex = _workingScenarios.Count - 1;
        ReloadScenarioList(); ShowSelectedScenario(); SetWindowText(_status, "Настройте новый сценарий и нажмите «Сохранить».");
    }

    private void SelectScenario()
    {
        if (_loading) return;
        int index = (int)SendMessage(_scenarioList, LB_GETCURSEL, nint.Zero, nint.Zero);
        if (index == _selectedIndex) return;
        CaptureEditorDraft(); _selectedIndex = index; ShowSelectedScenario(); SetWindowText(_status, string.Empty);
    }

    private void RefreshDisplayChoices()
    {
        if (_loading || _selectedIndex < 0) return;
        CaptureEditorDraft(); ShowSelectedScenario();
    }

    private void DeleteScenario()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _workingScenarios.Count) return;
        ScenarioDefinition scenario = _workingScenarios[_selectedIndex];
        if (MessageBox(_window, $"Удалить сценарий «{scenario.Name}»?", "RoomSwitcher", MB_YESNO | MB_ICONWARNING) != IdYes) return;
        _workingScenarios.RemoveAt(_selectedIndex); _settings.Current.Scenarios.RemoveAll(item => item.Id == scenario.Id);
        if (_settings.Current.ActiveScenarioId == scenario.Id) _settings.Current.ActiveScenarioId = null;
        if (_settings.Current.StartupScenarioId == scenario.Id) _settings.Current.StartupScenarioId = null;
        _settings.Save(); _tray.Refresh();
        _selectedIndex = _workingScenarios.Count == 0 ? -1 : Math.Min(_selectedIndex, _workingScenarios.Count - 1);
        ReloadScenarioList(); ShowSelectedScenario(); SetWindowText(_status, "Сценарий удалён.");
    }

    private bool SaveEditor()
    {
        CaptureEditorDraft();
        if (_selectedIndex < 0 || _selectedIndex >= _workingScenarios.Count) { SetWindowText(_status, "Создайте сценарий."); return false; }
        ScenarioDefinition current = _workingScenarios[_selectedIndex];
        if (string.IsNullOrWhiteSpace(current.Name)) { SetWindowText(_status, "Введите название сценария."); return false; }
        if (current.DisplayIds.Count == 0) { SetWindowText(_status, "Выберите монитор 1."); return false; }
        if (current.DisplayIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() != current.DisplayIds.Count) { SetWindowText(_status, "Мониторы в одном сценарии не должны повторяться."); return false; }
        if (string.IsNullOrWhiteSpace(current.AudioDeviceId)) { SetWindowText(_status, "Выберите аудиоустройство."); return false; }
        if (_workingScenarios.Any(s => !s.IsComplete)) { SetWindowText(_status, "Завершите настройку всех созданных сценариев."); return false; }
        PersistWorkingScenarios(); ReloadScenarioList(); SetWindowText(_status, "Сценарии сохранены."); return true;
    }

    private void PersistWorkingScenarios()
    {
        _settings.Current.Scenarios = _workingScenarios.Select(s => s.Clone()).ToList();
        if (_settings.Current.ActiveScenarioId.HasValue && _settings.Current.Scenarios.All(s => s.Id != _settings.Current.ActiveScenarioId.Value)) _settings.Current.ActiveScenarioId = null;
        if (_settings.Current.StartupScenarioId.HasValue && _settings.Current.Scenarios.All(s => s.Id != _settings.Current.StartupScenarioId.Value)) _settings.Current.StartupScenarioId = null;
        _settings.Save(); _tray.Refresh();
    }

    private async Task SaveAndApplyAsync()
    {
        if (!SaveEditor()) return;
        SetBusy(true); try { await _tray.ApplyScenarioAsync(_workingScenarios[_selectedIndex].Id); } finally { SetBusy(false); }
    }

    private void SaveAndOpenDisplaySettings()
    {
        if (!SaveEditor()) return;
        try { Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); }
        catch (Exception ex) { SettingsStore.Log(ex); SetWindowText(_status, $"Не удалось открыть параметры экрана: {ex.Message}"); }
    }

    private void SetEditorEnabled(bool enabled)
    {
        EnableWindow(_name, enabled); foreach (nint combo in _monitorCombos) EnableWindow(combo, enabled);
        EnableWindow(_audio, enabled); EnableWindow(_volume, enabled); EnableWindow(_delete, enabled); EnableWindow(_save, enabled); EnableWindow(_saveApply, enabled); EnableWindow(_saveOpenDisplay, enabled);
    }

    private void SetBusy(bool busy)
    {
        EnableWindow(_refresh, !busy); bool enabled = !busy && _selectedIndex >= 0;
        EnableWindow(_save, enabled); EnableWindow(_saveApply, enabled); EnableWindow(_saveOpenDisplay, enabled);
    }

    private static string GetText(nint window) { int length = GetWindowTextLength(window); var value = new StringBuilder(length + 1); GetWindowText(window, value, value.Capacity); return value.ToString(); }

    private nint HandleMessage(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WM_COMMAND)
        {
            ulong command = wParam.ToUInt64(); int id = (int)(command & 0xFFFF), notification = (int)((command >> 16) & 0xFFFF);
            if (id == IdNavGeneral) SetPage(Page.General);
            else if (id == IdNavScenarios) SetPage(Page.Scenarios);
            else if (id == IdSaveGeneral) SaveGeneralSettings();
            else if (id == IdStartupMode && notification == CBN_SELCHANGE) UpdateStartupScenarioEnabled();
            else if (id == IdScenarioList && notification == LBN_SELCHANGE) SelectScenario();
            else if (id == IdCreate) CreateScenario();
            else if (id == IdDelete) DeleteScenario();
            else if (id >= IdMonitor1 && id < IdMonitor1 + 4 && notification == CBN_SELCHANGE) RefreshDisplayChoices();
            else if (id == IdRefresh) _ = ReloadDevicesAsync();
            else if (id == IdSave) SaveEditor();
            else if (id == IdSaveApply) _ = SaveAndApplyAsync();
            else if (id == IdSaveOpenDisplay) SaveAndOpenDisplaySettings();
            else if (id == IdClose) DestroyWindow(window);
            return nint.Zero;
        }
        if (message == WM_CLOSE) { DestroyWindow(window); return nint.Zero; }
        if (message == WM_NCDESTROY) { _window = nint.Zero; if (_selfHandle.IsAllocated) _selfHandle.Free(); Closed?.Invoke(this, EventArgs.Empty); }
        return DefWindowProc(window, message, wParam, lParam);
    }

    private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        if (message == WM_NCCREATE) { CREATESTRUCT create = Marshal.PtrToStructure<CREATESTRUCT>(lParam); SetWindowLongPtr(window, -21, create.lpCreateParams); }
        nint pointer = GetWindowLongPtr(window, -21);
        if (pointer != nint.Zero && GCHandle.FromIntPtr(pointer).Target is SettingsWindow settings) return settings.HandleMessage(window, message, wParam, lParam);
        return DefWindowProc(window, message, wParam, lParam);
    }

    public void Dispose() { if (_window != nint.Zero) DestroyWindow(_window); if (_selfHandle.IsAllocated) _selfHandle.Free(); }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate nint WindowProcedure(nint window, uint message, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct WNDCLASSEX { public uint cbSize, style; public WindowProcedure lpfnWndProc; public int cbClsExtra, cbWndExtra; public nint hInstance, hIcon, hCursor, hbrBackground; public string? lpszMenuName, lpszClassName; public nint hIconSm; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CREATESTRUCT { public nint lpCreateParams, hInstance, hMenu, hwndParent; public int cy, cx, y, x, style; public nint lpszName, lpszClass; public uint dwExStyle; }
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
