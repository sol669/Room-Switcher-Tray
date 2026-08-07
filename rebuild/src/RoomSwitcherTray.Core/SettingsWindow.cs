using RoomSwitcherTray.Core.Services;
using System.Runtime.InteropServices;
using System.Text;

namespace RoomSwitcherTray.Core;

/// <summary>
/// Нативное Win32-окно базовой настройки. Оно не зависит от XAML и поэтому
/// не может обрушить трей из-за ошибки загрузки визуальных ресурсов WinUI.
/// </summary>
public sealed class SettingsWindow : IDisposable
{
    private const string WindowClass = "sol669.RoomSwitcherTray.Core.Settings";
    private const int IdName1 = 101;
    private const int IdDisplay1 = 102;
    private const int IdAudio1 = 103;
    private const int IdName2 = 201;
    private const int IdDisplay2 = 202;
    private const int IdAudio2 = 203;
    private const int IdRefresh = 301;
    private const int IdSave = 302;
    private const int IdCancel = 303;
    private const uint WM_COMMAND = 0x0111;
    private const uint WM_CLOSE = 0x0010;
    private const uint WM_NCCREATE = 0x0081;
    private const uint WM_NCDESTROY = 0x0082;
    private const uint WM_SETFONT = 0x0030;
    private const uint CB_ADDSTRING = 0x0143;
    private const uint CB_GETCURSEL = 0x0147;
    private const uint CB_RESETCONTENT = 0x014B;
    private const uint CB_SETCURSEL = 0x014E;
    private const int WS_OVERLAPPED = 0x00000000;
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_SYSMENU = 0x00080000;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int WS_VISIBLE = 0x10000000;
    private const int WS_CHILD = 0x40000000;
    private const int WS_TABSTOP = 0x00010000;
    private const int WS_VSCROLL = 0x00200000;
    private const int ES_AUTOHSCROLL = 0x0080;
    private const int CBS_DROPDOWNLIST = 0x0003;
    private const int BS_DEFPUSHBUTTON = 0x0001;
    private const int SW_SHOWNORMAL = 1;
    private const int COLOR_WINDOW = 5;
    private const int IDC_ARROW = 32512;
    private const int DEFAULT_GUI_FONT = 17;

    private static readonly WindowProcedure StaticWindowProcedure = WindowProc;
    private static readonly object RegistrationLock = new();
    private static bool _registered;

    private readonly SettingsStore _settings;
    private readonly TrayService _tray;
    private nint _window;
    private nint _name1;
    private nint _display1;
    private nint _audio1;
    private nint _name2;
    private nint _display2;
    private nint _audio2;
    private nint _status;
    private nint _refresh;
    private nint _save;
    private GCHandle _selfHandle;
    private IReadOnlyList<DisplayDevice> _displays = [];
    private IReadOnlyList<AudioDevice> _audioDevices = [];

    public event EventHandler? Closed;

    public SettingsWindow(SettingsStore settings, TrayService tray)
    {
        _settings = settings;
        _tray = tray;
    }

    public void Activate()
    {
        if (_window == nint.Zero)
            CreateWindow();
        else
        {
            ShowWindow(_window, SW_SHOWNORMAL);
            SetForegroundWindow(_window);
        }
    }

    private void CreateWindow()
    {
        EnsureRegistered();
        _selfHandle = GCHandle.Alloc(this);
        int width = 720;
        int height = 640;
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
        _ = ReloadDevicesAsync();
        SetForegroundWindow(_window);
    }

    private static void EnsureRegistered()
    {
        lock (RegistrationLock)
        {
            if (_registered) return;
            var windowClass = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = StaticWindowProcedure,
                hInstance = GetModuleHandle(null),
                hCursor = LoadCursor(nint.Zero, (nint)IDC_ARROW),
                hbrBackground = (nint)(COLOR_WINDOW + 1),
                lpszClassName = WindowClass
            };
            ushort atom = RegisterClassEx(ref windowClass);
            if (atom == 0 && Marshal.GetLastWin32Error() != 1410)
                throw new InvalidOperationException($"Не удалось зарегистрировать окно: {Marshal.GetLastWin32Error()}");
            _registered = true;
        }
    }

    private void CreateControls()
    {
        nint font = GetStockObject(DEFAULT_GUI_FONT);
        AddStatic("Настройте два сценария переключения", 24, 20, 650, 28, font);
        AddStatic("Сценарий 1", 24, 64, 180, 24, font);
        AddStatic("Название", 38, 104, 130, 24, font);
        _name1 = AddControl("EDIT", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
            180, 100, 490, 28, IdName1, font);
        AddStatic("Дисплей", 38, 146, 130, 24, font);
        _display1 = AddControl("COMBOBOX", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            180, 142, 490, 240, IdDisplay1, font);
        AddStatic("Аудиоустройство", 38, 188, 130, 24, font);
        _audio1 = AddControl("COMBOBOX", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            180, 184, 490, 240, IdAudio1, font);

        AddStatic("Сценарий 2", 24, 244, 180, 24, font);
        AddStatic("Название", 38, 284, 130, 24, font);
        _name2 = AddControl("EDIT", string.Empty, WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
            180, 280, 490, 28, IdName2, font);
        AddStatic("Дисплей", 38, 326, 130, 24, font);
        _display2 = AddControl("COMBOBOX", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            180, 322, 490, 240, IdDisplay2, font);
        AddStatic("Аудиоустройство", 38, 368, 130, 24, font);
        _audio2 = AddControl("COMBOBOX", string.Empty,
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | CBS_DROPDOWNLIST,
            180, 364, 490, 240, IdAudio2, font);

        _status = AddStatic(string.Empty, 24, 420, 646, 44, font);
        _refresh = AddControl("BUTTON", "Обновить список устройств",
            WS_CHILD | WS_VISIBLE | WS_TABSTOP, 24, 480, 240, 36, IdRefresh, font);
        AddControl("BUTTON", "Отмена", WS_CHILD | WS_VISIBLE | WS_TABSTOP,
            430, 540, 110, 38, IdCancel, font);
        _save = AddControl("BUTTON", "Сохранить",
            WS_CHILD | WS_VISIBLE | WS_TABSTOP | BS_DEFPUSHBUTTON,
            552, 540, 118, 38, IdSave, font);
    }

    private nint AddStatic(string text, int x, int y, int width, int height, nint font) =>
        AddControl("STATIC", text, WS_CHILD | WS_VISIBLE, x, y, width, height, 0, font);

    private nint AddControl(string className, string text, int style,
        int x, int y, int width, int height, int id, nint font)
    {
        nint control = CreateWindowEx(0, className, text, style, x, y, width, height,
            _window, (nint)id, GetModuleHandle(null), nint.Zero);
        if (control != nint.Zero)
            SendMessage(control, WM_SETFONT, font, (nint)1);
        return control;
    }

    private async Task ReloadDevicesAsync()
    {
        SetBusy(true);
        SetWindowText(_status, "Поиск устройств…");
        try
        {
            string name1 = GetText(_name1);
            string name2 = GetText(_name2);
            string? display1 = SelectedDisplay(_display1)?.Id ?? _settings.Current.Scenario1?.DisplayId;
            string? display2 = SelectedDisplay(_display2)?.Id ?? _settings.Current.Scenario2?.DisplayId;
            string? audio1 = SelectedAudio(_audio1)?.Id ?? _settings.Current.Scenario1?.AudioDeviceId;
            string? audio2 = SelectedAudio(_audio2)?.Id ?? _settings.Current.Scenario2?.AudioDeviceId;
            _displays = await Task.Run(App.Displays.GetDisplays);
            _audioDevices = await App.Audio.GetRenderDevicesAsync();
            BindCombo(_display1, _displays, display1);
            BindCombo(_display2, _displays, display2);
            BindCombo(_audio1, _audioDevices, audio1);
            BindCombo(_audio2, _audioDevices, audio2);
            SetWindowText(_name1, name1.Length > 0 ? name1 : _settings.Current.Scenario1?.Name ?? string.Empty);
            SetWindowText(_name2, name2.Length > 0 ? name2 : _settings.Current.Scenario2?.Name ?? string.Empty);
            SetWindowText(_status, _displays.Count == 0 || _audioDevices.Count == 0
                ? "Не удалось найти все необходимые устройства."
                : string.Empty);
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            SetWindowText(_status, $"Не удалось получить устройства: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static void BindCombo<T>(nint combo, IReadOnlyList<T> items, string? selectedId)
    {
        SendMessage(combo, CB_RESETCONTENT, nint.Zero, nint.Zero);
        int selected = -1;
        for (int index = 0; index < items.Count; index++)
        {
            T item = items[index];
            SendMessageString(combo, CB_ADDSTRING, nint.Zero, item?.ToString() ?? string.Empty);
            string id = item switch
            {
                DisplayDevice display => display.Id,
                AudioDevice audio => audio.Id,
                _ => string.Empty
            };
            if (id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) selected = index;
        }
        if (selected < 0 && items.Count > 0) selected = 0;
        SendMessage(combo, CB_SETCURSEL, (nint)selected, nint.Zero);
    }

    private DisplayDevice? SelectedDisplay(nint combo)
    {
        int index = (int)SendMessage(combo, CB_GETCURSEL, nint.Zero, nint.Zero);
        return index >= 0 && index < _displays.Count ? _displays[index] : null;
    }

    private AudioDevice? SelectedAudio(nint combo)
    {
        int index = (int)SendMessage(combo, CB_GETCURSEL, nint.Zero, nint.Zero);
        return index >= 0 && index < _audioDevices.Count ? _audioDevices[index] : null;
    }

    private void Save()
    {
        var first = new ScenarioDefinition
        {
            Name = GetText(_name1).Trim(),
            DisplayId = SelectedDisplay(_display1)?.Id ?? string.Empty,
            AudioDeviceId = SelectedAudio(_audio1)?.Id ?? string.Empty
        };
        var second = new ScenarioDefinition
        {
            Name = GetText(_name2).Trim(),
            DisplayId = SelectedDisplay(_display2)?.Id ?? string.Empty,
            AudioDeviceId = SelectedAudio(_audio2)?.Id ?? string.Empty
        };
        if (!first.IsComplete || !second.IsComplete)
        {
            SetWindowText(_status, "Для обоих сценариев укажите название, дисплей и аудиоустройство.");
            return;
        }
        try
        {
            _settings.Current.Scenario1 = first;
            _settings.Current.Scenario2 = second;
            _settings.Save();
            _tray.Refresh();
            DestroyWindow(_window);
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            SetWindowText(_status, $"Не удалось сохранить настройки: {ex.Message}");
        }
    }

    private void SetBusy(bool busy)
    {
        EnableWindow(_refresh, !busy);
        EnableWindow(_save, !busy);
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
            int id = unchecked((int)(wParam.ToUInt64() & 0xFFFF));
            if (id == IdRefresh) _ = ReloadDevicesAsync();
            else if (id == IdSave) Save();
            else if (id == IdCancel) DestroyWindow(window);
            return nint.Zero;
        }
        if (message == WM_CLOSE)
        {
            DestroyWindow(window);
            return nint.Zero;
        }
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
            if (handle.Target is SettingsWindow settings)
                return settings.HandleMessage(window, message, wParam, lParam);
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
        public uint cbSize, style;
        public WindowProcedure lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public nint hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREATESTRUCT
    {
        public nint lpCreateParams, hInstance, hMenu, hwndParent;
        public int cy, cx, y, x;
        public int style;
        public nint lpszName, lpszClass;
        public uint dwExStyle;
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
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
}
