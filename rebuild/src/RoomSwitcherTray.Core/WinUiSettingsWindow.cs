using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using RoomSwitcherTray.Core.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RoomSwitcherTray.Core;

/// <summary>The WinUI settings shell. Services and settings storage stay independent from this view.</summary>
public sealed class WinUiSettingsWindow : Window, IDisposable
{
    private readonly SettingsStore _settings;
    private readonly TrayService _tray;
    private readonly List<ScenarioDefinition> _scenarios;
    private readonly List<DisplayDevice> _displays = [];
    private readonly List<AudioDevice> _audio = [];
    private readonly Grid _root = new();
    private readonly ContentControl _pageHost = new();
    private readonly Dictionary<string, Button> _navButtons = [];
    private ListView? _scenarioList, _deviceList;
    private TextBox? _scenarioName, _deviceAlias;
    private ComboBox[] _monitorBoxes = new ComboBox[4];
    private ComboBox? _audioBox, _volumeBox, _iconBox, _startupChoiceBox, _themeBox, _languageBox;
    private ToggleSwitch? _autostartToggle;
    private TextBlock? _hotKeyHint, _hotKeyTitle, _autostartState;
    private Button? _hotKeyCaptureButton;
    private ScenarioDefinition? _selectedScenario;
    private DeviceItem? _selectedDevice;
    private string _currentPage = "general";
    private bool _loading, _capturingHotKey;

    public event EventHandler? ClosedByUser;

    public WinUiSettingsWindow(SettingsStore settings, TrayService tray)
    {
        _settings = settings;
        _tray = tray;
        _scenarios = settings.Current.Scenarios.Select(item => item.Clone()).ToList();
        _root.IsTabStop = true;
        _pageHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _pageHost.VerticalContentAlignment = VerticalAlignment.Stretch;
        Title = "RoomSwitcher";
        try { SystemBackdrop = new MicaBackdrop(); } catch { }
        // AppWindow uses physical pixels.  A wider fixed size leaves room for the
        // RoomSwitcher navigation pane and the Shutdown-style horizontal rows,
        // including on Windows installations with high display scaling.
        ConfigureWindow();
        Closed += (_, _) => ClosedByUser?.Invoke(this, EventArgs.Empty);
        BuildShell("general");
        _ = LoadDevicesAsync();
    }

    public void Dispose() => Close();

    // Mirrors Shutdown's sizing model: logical dimensions are converted to the
    // DPI of the monitor where the settings window is opened.
    private void ConfigureWindow()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow window = AppWindow.GetFromWindowId(id);
        NativeTheme.Apply(hwnd, _settings.Current.Theme);
        const int logicalWidth = 980;  // Shutdown 1.1.2 width (760) plus RoomSwitcher's navigation pane.
        const int logicalHeight = 900;
        double scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
        GetCursorPos(out NativePoint cursor);
        DisplayArea area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary);
        RectInt32 work = area.WorkArea;
        int width = Math.Min((int)Math.Round(logicalWidth * scale), Math.Max(840, work.Width - 48));
        int height = Math.Min((int)Math.Round(logicalHeight * scale), Math.Max(650, work.Height - 48));
        window.MoveAndResize(new RectInt32(
            work.X + Math.Max(0, (work.Width - width) / 2),
            work.Y + Math.Max(0, (work.Height - height) / 2), width, height));
        window.SetPresenter(AppWindowPresenterKind.Overlapped);
        if (window.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);

    private bool English => _settings.Current.Language == AppLanguage.English;
    private string T(string key) => (English, key) switch
    {
        (true, "General") => "General", (true, "Scenarios") => "Scenarios", (true, "Devices") => "Devices",
        (true, "Theme") => "Theme", (true, "Language") => "Language", (true, "Behavior") => "Behavior", (true, "System") => "System",
        (true, "Autostart") => "Autostart", (true, "StartupScenario") => "Scenario at startup",
        (true, "Hotkey") => "Hotkey", (true, "Change") => "Change…", (true, "Save") => "Save", (true, "Cancel") => "Cancel",
        (true, "LastLoaded") => "Last loaded",
        (true, "Create") => "New scenario", (true, "Delete") => "Delete scenario", (true, "Name") => "Name",
        (true, "Icon") => "Icon", (true, "Monitor") => "Monitor", (true, "Audio") => "Audio device",
        (true, "Volume") => "Volume", (true, "Refresh") => "Refresh devices", (true, "Apply") => "Apply",
        (true, "OpenDisplay") => "Save and open Display settings", (true, "SystemName") => "System name",
        (true, "FriendlyName") => "Name in RoomSwitcher", (true, "SaveName") => "Save name",
        (true, "NoChange") => "Don't change", (true, "None") => "None", (true, "Settings") => "Settings", (true, "Footer") => "RoomSwitcher 0.7.9 · sol669 ·",
        (false, "General") => "Основные", (false, "Scenarios") => "Сценарии", (false, "Devices") => "Устройства",
        (false, "Theme") => "Тема", (false, "Language") => "Язык", (false, "Behavior") => "Поведение", (false, "System") => "Система",
        (false, "Autostart") => "Автозапуск", (false, "StartupScenario") => "Сценарий при запуске",
        (false, "Hotkey") => "Горячая клавиша", (false, "Change") => "Изменить…", (false, "Save") => "Сохранить", (false, "Cancel") => "Отмена",
        (false, "LastLoaded") => "Последний загруженный",
        (false, "Create") => "Новый сценарий", (false, "Delete") => "Удалить сценарий", (false, "Name") => "Название",
        (false, "Icon") => "Иконка", (false, "Monitor") => "Монитор", (false, "Audio") => "Аудиоустройство",
        (false, "Volume") => "Громкость", (false, "Refresh") => "Обновить устройства", (false, "Apply") => "Применить",
        (false, "OpenDisplay") => "Сохранить и настроить экраны", (false, "SystemName") => "Системное имя",
        (false, "FriendlyName") => "Имя в RoomSwitcher", (false, "SaveName") => "Сохранить имя",
        (false, "NoChange") => "Не менять", (false, "None") => "Нет", (false, "Settings") => "Настройки", (false, "Footer") => "RoomSwitcher 0.7.9 · sol669 ·",
        _ => key
    };

    private void BuildShell(string page)
    {
        _currentPage = page;
        _navButtons.Clear();
        _root.RowDefinitions.Clear(); _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var main = new Grid(); main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) }); main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var menu = new StackPanel { Spacing = 5, Margin = new Thickness(20, 20, 16, 12) };
        menu.Children.Add(new TextBlock { Text = T("Settings"), FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 36) });
        menu.Children.Add(NavButton(T("General"), "general"));
        menu.Children.Add(NavButton(T("Scenarios"), "scenarios"));
        menu.Children.Add(NavButton(T("Devices"), "devices"));
        Grid.SetColumn(menu, 0); main.Children.Add(menu); Grid.SetColumn(_pageHost, 1); main.Children.Add(_pageHost);
        Grid.SetRow(main, 0);
        var footer = new Grid { Margin = new Thickness(244, 10, 24, 18), ColumnSpacing = 10, Padding = new Thickness(0, 2, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var footerInfo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Opacity = .68 };
        footerInfo.Children.Add(new TextBlock { Text = T("Footer"), VerticalAlignment = VerticalAlignment.Center });
        footerInfo.Children.Add(new HyperlinkButton { Content = "GitHub", NavigateUri = new Uri("https://github.com/sol669/Room-Switcher-Tray"), Padding = new Thickness(0) });
        var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        var cancel = new Button { Content = T("Cancel"), MinWidth = 110 }; cancel.Click += (_, _) => Close();
        var save = new Button { Content = T("Save"), MinWidth = 110, Background = AccentBrush(), Foreground = AccentTextBrush() }; save.Click += (_, _) => { if (_currentPage == "general") SaveGeneral(); };
        footerButtons.Children.Add(cancel); footerButtons.Children.Add(save); Grid.SetColumn(footerButtons, 1);
        footer.Children.Add(footerInfo); footer.Children.Add(footerButtons);
        Grid.SetRow(footer, 1);
        _root.Children.Clear(); _root.Children.Add(main); _root.Children.Add(footer);
        _root.KeyDown -= RootKeyDown; _root.KeyDown += RootKeyDown;
        Content = _root;
        ShowPage(page);
        ApplyTheme();
    }

    private Button NavButton(string text, string page)
    {
        bool selected = page == _currentPage;
        var button = new Button
        {
            Content = text,
            Height = 46,
            Padding = new Thickness(14, 0, 14, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = selected ? AccentBrush() : ThemeBrush("SubtleFillColorTransparentBrush", Colors.Transparent),
            Foreground = selected ? AccentTextBrush() : ThemeBrush("TextFillColorPrimaryBrush", IsDark() ? Colors.White : Colors.Black)
        };
        _navButtons[page] = button;
        button.Click += (_, _) => NavigateTo(page);
        return button;
    }
    private void NavigateTo(string page)
    {
        if (page == _currentPage) return;
        ShowPage(page);
        RefreshNavigationSelection();
    }
    private void RefreshNavigationSelection()
    {
        foreach ((string page, Button button) in _navButtons)
        {
            bool selected = page == _currentPage;
            button.Background = selected ? AccentBrush() : ThemeBrush("SubtleFillColorTransparentBrush", Colors.Transparent);
            button.Foreground = selected ? AccentTextBrush() : ThemeBrush("TextFillColorPrimaryBrush", IsDark() ? Colors.White : Colors.Black);
        }
    }
    private void ShowPage(string page)
    {
        _currentPage = page;
        _pageHost.Content = page switch { "scenarios" => BuildScenariosPage(), "devices" => BuildDevicesPage(), _ => BuildGeneralPage() };
    }
    private static StackPanel Panel() => new() { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Stretch, Spacing = 5, Margin = new Thickness(24, 20, 24, 0) };
    private static TextBlock Heading(string text) => new() { Text = text, FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
    private static TextBlock Label(string text) => new() { Text = text, Opacity = .72 };

    private UIElement BuildGeneralPage()
    {
        var panel = Panel();
        panel.Children.Add(Section(T("Behavior"), first: true));
        _startupChoiceBox = SettingsComboBox(new ComboBox { DisplayMemberPath = "Name" });
        var startupChoices = new List<StartupChoice> { new(null, T("LastLoaded")) };
        startupChoices.AddRange(_scenarios.Where(s => s.IsComplete).Select(s => new StartupChoice(s.Id, s.Name)));
        _startupChoiceBox.ItemsSource = startupChoices;
        _startupChoiceBox.SelectedIndex = _settings.Current.StartupScenarioMode == StartupScenarioMode.AlwaysUseScenario
            ? Math.Max(0, startupChoices.FindIndex(choice => choice.Id == _settings.Current.StartupScenarioId)) : 0;
        panel.Children.Add(SettingRow(T("StartupScenario"), _startupChoiceBox));
        var hotkeyLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
        _hotKeyCaptureButton = new Button { Content = T("Change"), MinWidth = 110 }; _hotKeyCaptureButton.Click += (_, _) => BeginCapture(_hotKeyCaptureButton); hotkeyLine.Children.Add(_hotKeyCaptureButton);
        _hotKeyTitle = new TextBlock { Text = $"{T("Hotkey")} — {TrayService.FormatHotKey(_settings.Current.SwitchScenarioHotKey)}", VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(SettingRow(_hotKeyTitle, hotkeyLine));
        _hotKeyHint = new TextBlock { Opacity = .68, Margin = new Thickness(16, 0, 0, 0) }; panel.Children.Add(_hotKeyHint);
        panel.Children.Add(Section(T("System")));
        _autostartToggle = new ToggleSwitch { IsOn = StartupService.IsEnabled(), MinWidth = 0, Width = 44, HorizontalAlignment = HorizontalAlignment.Right, OffContent = string.Empty, OnContent = string.Empty };
        _autostartState = new TextBlock { MinWidth = 72, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Opacity = .72 };
        _autostartToggle.Toggled += (_, _) => UpdateAutostartState();
        UpdateAutostartState();
        panel.Children.Add(ToggleSettingRow(T("Autostart"), _autostartState, _autostartToggle));
        _themeBox = SettingsComboBox(new ComboBox { ItemsSource = English ? new[] { "Like Windows", "Light", "Dark" } : new[] { "Как в Windows", "Светлая", "Тёмная" }, SelectedIndex = (int)_settings.Current.Theme });
        panel.Children.Add(SettingRow(T("Theme"), _themeBox));
        _languageBox = SettingsComboBox(new ComboBox { ItemsSource = new[] { "Русский", "English" }, SelectedIndex = (int)_settings.Current.Language });
        panel.Children.Add(SettingRow(T("Language"), _languageBox));
        return panel;
    }

    private static TextBlock Section(string text, bool first = false) => new() { Text = text.ToUpperInvariant(), FontSize = 12, FontWeight = FontWeights.SemiBold, Opacity = .68, Margin = new Thickness(2, first ? 0 : 13, 0, 1) };

    private ComboBox SettingsComboBox(ComboBox comboBox)
    {
        comboBox.Width = 280;
        comboBox.Height = 34;
        comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        comboBox.VerticalAlignment = VerticalAlignment.Center;
        comboBox.CornerRadius = new CornerRadius(4);
        comboBox.BorderThickness = new Thickness(1);
        comboBox.Background = ThemeBrush("ControlFillColorDefaultBrush", IsDark() ? Color.FromArgb(255, 50, 50, 53) : Color.FromArgb(255, 249, 249, 249));
        comboBox.BorderBrush = ThemeBrush("ControlStrokeColorDefaultBrush", IsDark() ? Color.FromArgb(255, 82, 82, 86) : Color.FromArgb(255, 160, 160, 160));
        return comboBox;
    }
    private Border SettingRow(string title, FrameworkElement control)
        => SettingRow(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, control);
    private Border SettingRow(FrameworkElement title, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(title);
        Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center; grid.Children.Add(control);
        return SettingsCard(grid);
    }

    private Border ToggleSettingRow(string title, TextBlock state, ToggleSwitch toggle)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(state, 1); grid.Children.Add(state);
        Grid.SetColumn(toggle, 2); grid.Children.Add(toggle);
        return SettingsCard(grid);
    }

    private Border SettingsCard(Grid content) => new()
    {
        Child = content,
        Background = ThemeBrush("CardBackgroundFillColorDefaultBrush", IsDark() ? Color.FromArgb(255, 45, 45, 49) : Color.FromArgb(255, 250, 250, 250)),
        BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush", IsDark() ? Color.FromArgb(255, 55, 55, 60) : Color.FromArgb(255, 225, 225, 225)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        MinHeight = 46,
        Padding = new Thickness(14, 5, 14, 5)
    };

    private void UpdateAutostartState()
    {
        if (_autostartState is null || _autostartToggle is null) return;
        _autostartState.Text = _autostartToggle.IsOn ? (English ? "On" : "Вкл.") : (English ? "Off" : "Откл.");
    }

    private void ApplyTheme()
    {
        _root.RequestedTheme = _settings.Current.Theme switch { AppThemeMode.Light => ElementTheme.Light, AppThemeMode.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        NativeTheme.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), _settings.Current.Theme);
    }

    private bool IsDark() => _settings.Current.Theme == AppThemeMode.Dark || (_settings.Current.Theme == AppThemeMode.System && NativeTheme.IsSystemDark());
    private SolidColorBrush ThemeBrush(string key, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out object? resource))
        {
            if (resource is SolidColorBrush brush) return brush;
            if (resource is Color color) return new SolidColorBrush(color);
        }
        return new SolidColorBrush(fallback);
    }
    private SolidColorBrush AccentBrush()
    {
        Color fallback = new UISettings().GetColorValue(UIColorType.Accent);
        return ThemeBrush("AccentFillColorDefaultBrush", fallback);
    }
    private SolidColorBrush AccentTextBrush()
    {
        Color accent = new UISettings().GetColorValue(UIColorType.Accent);
        double luminance = .299 * accent.R + .587 * accent.G + .114 * accent.B;
        return ThemeBrush("TextOnAccentFillColorPrimaryBrush", luminance > 165 ? Colors.Black : Colors.White);
    }
    private void SaveGeneral()
    {
        if (_startupChoiceBox?.SelectedItem is not StartupChoice startup || _autostartToggle is null || _themeBox is null || _languageBox is null) return;
        StartupService.SetEnabled(_autostartToggle.IsOn);
        _settings.Current.StartWithWindows = _autostartToggle.IsOn;
        _settings.Current.StartupScenarioMode = startup.Id is null ? StartupScenarioMode.RestoreLastScenario : StartupScenarioMode.AlwaysUseScenario;
        _settings.Current.StartupScenarioId = startup.Id;
        _settings.Current.Theme = (AppThemeMode)_themeBox.SelectedIndex;
        _settings.Current.Language = (AppLanguage)_languageBox.SelectedIndex;
        _settings.Save();
        _tray.Refresh();
        Close();
    }

    private void BeginCapture(Button button) { _capturingHotKey = true; button.Content = English ? "Press shortcut…" : "Нажмите сочетание…"; if (_hotKeyHint is not null) _hotKeyHint.Text = English ? "Use Ctrl, Alt, Shift or Win with another key." : "Нажмите Ctrl, Alt, Shift или Win вместе с другой клавишей."; _root.Focus(FocusState.Programmatic); }
    private void RootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_capturingHotKey || args.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
        uint modifiers = 0; if (Down(VirtualKey.Control)) modifiers |= HotKeyDefinition.Control; if (Down(VirtualKey.Menu)) modifiers |= HotKeyDefinition.Alt; if (Down(VirtualKey.Shift)) modifiers |= HotKeyDefinition.Shift; if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= HotKeyDefinition.Win;
        var hotKey = new HotKeyDefinition { Modifiers = modifiers, VirtualKey = (uint)args.Key };
        if (_tray.TryUpdateHotKey(hotKey, out string error))
        {
            _settings.Current.SwitchScenarioHotKey = hotKey; _settings.Save(); _tray.Refresh(); _capturingHotKey = false;
            if (_hotKeyTitle is not null) _hotKeyTitle.Text = $"{T("Hotkey")} — {TrayService.FormatHotKey(hotKey)}";
            if (_hotKeyCaptureButton is not null) _hotKeyCaptureButton.Content = T("Change");
            if (_hotKeyHint is not null) _hotKeyHint.Text = English ? "Shortcut saved." : "Сочетание сохранено.";
        }
        else
        {
            _capturingHotKey = false;
            if (_hotKeyCaptureButton is not null) _hotKeyCaptureButton.Content = T("Change");
            if (_hotKeyHint is not null) _hotKeyHint.Text = error;
        }
        args.Handled = true;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern short GetKeyState(int key);
    private static bool Down(VirtualKey key) => (GetKeyState((int)key) & unchecked((short)0x8000)) != 0;

    private UIElement BuildScenariosPage()
    {
        var grid = new Grid { Margin = new Thickness(28), ColumnSpacing = 24 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Spacing = 10 }; var create = new Button { Content = "+ " + T("Create") }; create.Click += (_, _) => CreateScenario(); left.Children.Add(create);
        _scenarioList = new ListView { ItemsSource = _scenarios, DisplayMemberPath = "Name", SelectionMode = ListViewSelectionMode.Single, Height = 460 }; _scenarioList.SelectionChanged += (_, _) => { if (!_loading) { CaptureScenario(); _selectedScenario = _scenarioList.SelectedItem as ScenarioDefinition; ShowScenarioEditor(); } }; left.Children.Add(_scenarioList);
        var delete = new Button { Content = T("Delete") }; delete.Click += (_, _) => DeleteScenario(); left.Children.Add(delete); Grid.SetColumn(left, 0); grid.Children.Add(left);
        var editor = new StackPanel { Spacing = 10 }; Grid.SetColumn(editor, 1); grid.Children.Add(editor); _pageHost.Tag = editor;
        _selectedScenario ??= _scenarios.FirstOrDefault(); _scenarioList.SelectedItem = _selectedScenario; ShowScenarioEditor();
        return grid;
    }

    private void ShowScenarioEditor()
    {
        if (_pageHost.Tag is not StackPanel editor) return;
        editor.Children.Clear();
        if (_selectedScenario is null) { editor.Children.Add(Heading(T("Scenarios"))); editor.Children.Add(new TextBlock { Text = English ? "Create your first scenario." : "Создайте первый сценарий." }); return; }
        _loading = true; editor.Children.Add(Heading(T("Scenarios")));
        editor.Children.Add(Label(T("Name"))); _scenarioName = new TextBox { Text = _selectedScenario.Name }; editor.Children.Add(_scenarioName);
        editor.Children.Add(Label(T("Icon"))); _iconBox = new ComboBox { ItemsSource = IconChoices(), DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = _selectedScenario.Icon }; editor.Children.Add(_iconBox);
        for (int i = 0; i < 4; i++) { editor.Children.Add(Label($"{T("Monitor")} {i + 1}")); _monitorBoxes[i] = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id" }; _monitorBoxes[i].SelectionChanged += (_, _) => { if (!_loading) RefreshMonitorChoices(); }; editor.Children.Add(_monitorBoxes[i]); }
        editor.Children.Add(Label(T("Audio"))); _audioBox = new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id" }; BindAudio(); editor.Children.Add(_audioBox);
        editor.Children.Add(Label(T("Volume"))); _volumeBox = new ComboBox { ItemsSource = VolumeChoices(), DisplayMemberPath = "Name", SelectedValuePath = "Value", SelectedValue = _selectedScenario.VolumePercent }; editor.Children.Add(_volumeBox);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 12, 0, 0) }; var save = new Button { Content = T("Save") }; save.Click += (_, _) => SaveScenario(false); actions.Children.Add(save); var open = new Button { Content = T("OpenDisplay") }; open.Click += (_, _) => SaveScenario(true); actions.Children.Add(open); var apply = new Button { Content = T("Apply") }; apply.Click += async (_, _) => { if (SaveScenario(false)) await _tray.ApplyScenarioAsync(_selectedScenario.Id); }; actions.Children.Add(apply); editor.Children.Add(actions);
        RefreshMonitorChoices(); _loading = false;
    }

    private sealed record Choice(string Id, string Name); private sealed record IntChoice(int? Value, string Name); private sealed record IconChoice(ScenarioIcon Value, string Name); private sealed record DeviceItem(string Id, string SystemName, string Kind, string Name); private sealed record StartupChoice(Guid? Id, string Name);
    private IEnumerable<IconChoice> IconChoices() => new[] { new IconChoice(ScenarioIcon.Automatic, English ? "Automatic letters" : "Автоматические буквы"), new IconChoice(ScenarioIcon.Desktop, English ? "Desktop" : "Компьютер"), new IconChoice(ScenarioIcon.Television, English ? "Television" : "Телевизор"), new IconChoice(ScenarioIcon.Sofa, English ? "Sofa" : "Диван"), new IconChoice(ScenarioIcon.Gamepad, English ? "Gamepad" : "Геймпад") };
    private IEnumerable<IntChoice> VolumeChoices() => new int?[] { null, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }.Select(value => new IntChoice(value, value switch { null => T("NoChange"), 0 => "0% — mute", int p => $"{p}%" }));
    private void BindAudio()
    {
        if (_audioBox is null || _selectedScenario is null) return;
        _audioBox.ItemsSource = _audio.Select(device => new Choice(device.Id, DeviceAliasService.NameFor(_settings.Current, device.Id, device.DisplayName ?? device.Name))).ToList(); _audioBox.SelectedValue = _selectedScenario.AudioDeviceId;
    }
    private void RefreshMonitorChoices()
    {
        if (_selectedScenario is null) return; var selected = _monitorBoxes.Select(box => box.SelectedValue as string).ToArray();
        for (int i = 0; i < 4; i++) { string? own = selected[i]; var used = selected.Where((id, index) => index != i && !string.IsNullOrWhiteSpace(id)).ToHashSet(StringComparer.OrdinalIgnoreCase); var choices = _displays.Where(device => device.Id == own || !used.Contains(device.Id)).Select(device => new Choice(device.Id, DeviceAliasService.NameFor(_settings.Current, device.Id, device.Name))).ToList(); if (i > 0) choices.Insert(0, new Choice(string.Empty, T("None"))); _monitorBoxes[i].ItemsSource = choices; _monitorBoxes[i].SelectedValue = own ?? (i == 0 ? _selectedScenario.DisplayIds.FirstOrDefault() : string.Empty); }
    }
    private void CaptureScenario()
    {
        if (_selectedScenario is null || _scenarioName is null) return; _selectedScenario.Name = _scenarioName.Text.Trim(); _selectedScenario.Icon = _iconBox?.SelectedValue is ScenarioIcon icon ? icon : ScenarioIcon.Automatic; _selectedScenario.DisplayIds = _monitorBoxes.Select(box => box.SelectedValue as string).Where(id => !string.IsNullOrWhiteSpace(id)).Cast<string>().ToList(); if (_audioBox?.SelectedValue is string audioId) { _selectedScenario.AudioDeviceId = audioId; _selectedScenario.AudioDeviceContainerId = _audio.FirstOrDefault(item => item.Id == audioId)?.ContainerId?.ToString("D") ?? string.Empty; } _selectedScenario.VolumePercent = _volumeBox?.SelectedValue as int?;
    }
    private bool SaveScenario(bool openSettings)
    {
        CaptureScenario(); if (_selectedScenario is null || !_selectedScenario.IsComplete) return false; _settings.Current.Scenarios = _scenarios.Select(item => item.Clone()).ToList(); _settings.Save(); _tray.Refresh(); if (openSettings) Process.Start(new ProcessStartInfo("ms-settings:display") { UseShellExecute = true }); return true;
    }
    private void CreateScenario() { CaptureScenario(); if (_selectedScenario is not null && !_selectedScenario.IsComplete) return; var scenario = new ScenarioDefinition { Name = English ? "New scenario" : "Новый сценарий" }; _scenarios.Add(scenario); _selectedScenario = scenario; ShowPage("scenarios"); }
    private void DeleteScenario() { if (_selectedScenario is null) return; _scenarios.Remove(_selectedScenario); _settings.Current.Scenarios.RemoveAll(item => item.Id == _selectedScenario.Id); _settings.Save(); _selectedScenario = _scenarios.FirstOrDefault(); ShowPage("scenarios"); }

    private UIElement BuildDevicesPage()
    {
        var grid = new Grid { Margin = new Thickness(28), ColumnSpacing = 24 }; grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _deviceList = new ListView { SelectionMode = ListViewSelectionMode.Single, Height = 480 }; _deviceList.SelectionChanged += (_, _) => { _selectedDevice = _deviceList.SelectedItem as DeviceItem; ShowDeviceEditor(); }; Grid.SetColumn(_deviceList, 0); grid.Children.Add(_deviceList);
        var editor = new StackPanel { Spacing = 12 }; Grid.SetColumn(editor, 1); grid.Children.Add(editor); _pageHost.Tag = editor; ReloadDeviceList(); ShowDeviceEditor(); return grid;
    }
    private void ReloadDeviceList()
    {
        if (_deviceList is null) return; var items = _displays.Select(d => new DeviceItem(d.Id, d.Name, English ? "Display" : "Монитор", DeviceAliasService.NameFor(_settings.Current, d.Id, d.Name))).Concat(_audio.Select(a => new DeviceItem(a.Id, a.DisplayName ?? a.Name, English ? "Audio" : "Аудио", DeviceAliasService.NameFor(_settings.Current, a.Id, a.DisplayName ?? a.Name)))).OrderBy(item => item.Kind).ThenBy(item => item.Name).ToList(); _deviceList.ItemsSource = items; _deviceList.DisplayMemberPath = "Name"; _deviceList.SelectedItem = items.FirstOrDefault();
    }
    private void ShowDeviceEditor()
    {
        if (_pageHost.Tag is not StackPanel editor) return; editor.Children.Clear(); editor.Children.Add(Heading(T("Devices"))); if (_selectedDevice is null) return; editor.Children.Add(Label(T("SystemName"))); editor.Children.Add(new TextBlock { Text = _selectedDevice.SystemName }); editor.Children.Add(Label(T("FriendlyName"))); _deviceAlias = new TextBox { Text = _settings.Current.DeviceAliases.TryGetValue(_selectedDevice.Id, out string? alias) ? alias : string.Empty }; editor.Children.Add(_deviceAlias); var save = new Button { Content = T("SaveName"), HorizontalAlignment = HorizontalAlignment.Left }; save.Click += (_, _) => { DeviceAliasService.Set(_settings.Current, _selectedDevice.Id, _deviceAlias.Text); _settings.Save(); _tray.Refresh(); ReloadDeviceList(); }; editor.Children.Add(save);
    }
    private async Task LoadDevicesAsync()
    {
        try { _displays.Clear(); _displays.AddRange(await Task.Run(App.Displays.GetDisplays)); _audio.Clear(); _audio.AddRange(await App.Audio.GetVisibleRenderDevicesAsync(_displays, _scenarios.Cast<ScenarioDefinition?>().ToArray())); ShowPage(_currentPage); } catch (Exception ex) { SettingsStore.Log(ex); }
    }
}
