using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RoomSwitcherTray.Core.Services;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace RoomSwitcherTray.Core;

/// <summary>Fixed WinUI settings shell shared by general, device and scenario pages.</summary>
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
    private readonly Dictionary<string, string> _pendingAliases = new(StringComparer.OrdinalIgnoreCase);
    private StackPanel? _scenarioNavPanel;
    private Button? _saveButton;
    private Button? _deleteButton;
    private TextBlock? _hotKeyTitle;
    private TextBlock? _hotKeyHint;
    private Button? _hotKeyCaptureButton;
    private TextBlock? _autostartState;
    private ToggleSwitch? _autostartToggle;
    private ComboBox? _startupChoiceBox;
    private ScenarioDefinition? _draft;
    private ScenarioDefinition? _scenarioBaseline;
    private string[] _draftMonitorSlots = new string[4];
    private string _currentPage = "general";
    private bool _draftIsNew;
    private bool _loading;
    private bool _devicesLoaded;
    private bool _capturingHotKey;
    private bool _allowClose;
    private bool _dialogOpen;
    private bool _pendingStartWithWindows;
    private StartupScenarioMode _pendingStartupMode;
    private Guid? _pendingStartupScenarioId;
    private AppThemeMode _pendingTheme;
    private AppLanguage _pendingLanguage;
    private HotKeyDefinition _pendingHotKey = HotKeyDefinition.Default;

    public event EventHandler? ClosedByUser;

    public WinUiSettingsWindow(SettingsStore settings, TrayService tray, bool openNewScenario = false)
    {
        _settings = settings;
        _tray = tray;
        _scenarios = settings.Current.Scenarios.Select(item => item.Clone()).ToList();
        ResetPendingGeneral();
        ResetPendingAliases();
        _root.IsTabStop = true;
        _pageHost.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _pageHost.VerticalContentAlignment = VerticalAlignment.Stretch;
        Title = "RoomSwitcher";
        try { SystemBackdrop = new MicaBackdrop(); } catch { }

        AppWindow appWindow = ConfigureWindow();
        appWindow.Closing += (sender, args) =>
        {
            if (_allowClose || !HasCurrentChanges()) return;
            args.Cancel = true;
            _ = ConfirmCloseAsync();
        };
        Closed += (_, _) => ClosedByUser?.Invoke(this, EventArgs.Empty);

        BuildShell();
        _ = LoadDevicesAsync(openNewScenario);
    }

    public void Dispose() => Close();

    public void OpenNewScenarioDraft()
    {
        Activate();
        _ = OpenNewScenarioDraftAsync();
    }

    private AppWindow ConfigureWindow()
    {
        nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        AppWindow window = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        NativeTheme.Apply(hwnd, _settings.Current.Theme);
        const int logicalWidth = 900;
        const int logicalHeight = 820;
        double scale = Math.Max(1, GetDpiForWindow(hwnd) / 96.0);
        GetCursorPos(out NativePoint cursor);
        RectInt32 work = DisplayArea.GetFromPoint(
            new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min((int)Math.Round(logicalWidth * scale), Math.Max(780, work.Width - 48));
        int height = Math.Min((int)Math.Round(logicalHeight * scale), Math.Max(640, work.Height - 48));
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
        return window;
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern short GetKeyState(int key);

    private bool English => _settings.Current.Language == AppLanguage.English;

    private string T(string key) => (English, key) switch
    {
        (true, "Settings") => "Settings", (true, "Scenarios") => "Scenarios",
        (true, "General") => "General", (true, "Devices") => "Devices",
        (true, "NewScenario") => "New scenario", (true, "StartupScenario") => "Scenario at startup",
        (true, "LastLoaded") => "Last loaded", (true, "Hotkey") => "Hotkey",
        (true, "Change") => "Change…", (true, "Autostart") => "Autostart",
        (true, "Theme") => "Theme", (true, "Language") => "Language",
        (true, "Behavior") => "Behavior", (true, "System") => "System",
        (true, "Monitors") => "Monitors", (true, "AudioDevices") => "Audio devices",
        (true, "DeviceAlias") => "Name in RoomSwitcher", (true, "ScenarioName") => "Scenario name",
        (true, "ScenarioSettings") => "Scenario settings", (true, "Monitor") => "Monitor",
        (true, "Audio") => "Audio device", (true, "Volume") => "Volume",
        (true, "Sound") => "Sound", (true, "TrayIcon") => "Tray icon",
        (true, "ScenarioIcon") => "Scenario icon", (true, "Letters") => "Letters",
        (true, "None") => "None", (true, "NoChange") => "Don't change",
        (true, "Desktop") => "Computer", (true, "Television") => "Television",
        (true, "Sofa") => "Sofa", (true, "Gamepad") => "Gamepad",
        (true, "Close") => "Close", (true, "Save") => "Save", (true, "Delete") => "Delete",
        (true, "FooterVersion") => "RoomSwitcher 0.9.2",
        (false, "Settings") => "Настройки", (false, "Scenarios") => "Сценарии",
        (false, "General") => "Основные", (false, "Devices") => "Устройства",
        (false, "NewScenario") => "Новый сценарий", (false, "StartupScenario") => "Сценарий при запуске",
        (false, "LastLoaded") => "Последний загруженный", (false, "Hotkey") => "Горячая клавиша",
        (false, "Change") => "Изменить…", (false, "Autostart") => "Автозапуск",
        (false, "Theme") => "Тема", (false, "Language") => "Язык",
        (false, "Behavior") => "Поведение", (false, "System") => "Система",
        (false, "Monitors") => "Мониторы", (false, "AudioDevices") => "Аудиоустройства",
        (false, "DeviceAlias") => "Имя в RoomSwitcher", (false, "ScenarioName") => "Название сценария",
        (false, "ScenarioSettings") => "Настройки сценария", (false, "Monitor") => "Монитор",
        (false, "Audio") => "Аудиоустройство", (false, "Volume") => "Громкость",
        (false, "Sound") => "Звук", (false, "TrayIcon") => "Иконка в трее",
        (false, "ScenarioIcon") => "Иконка сценария", (false, "Letters") => "Литеры",
        (false, "None") => "Нет", (false, "NoChange") => "Не менять",
        (false, "Desktop") => "Компьютер", (false, "Television") => "Телевизор",
        (false, "Sofa") => "Диван", (false, "Gamepad") => "Геймпад",
        (false, "Close") => "Закрыть", (false, "Save") => "Сохранить", (false, "Delete") => "Удалить",
        (false, "FooterVersion") => "RoomSwitcher 0.9.2",
        _ => key
    };

    private void BuildShell()
    {
        _navButtons.Clear();
        _root.RowDefinitions.Clear();
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var main = new Grid();
        // The visible navigation cell is 244 px wide (280 minus its 20/16 margins),
        // exactly matching the value-control column used by every settings row.
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        main.Children.Add(BuildNavigation());
        Grid.SetColumn(_pageHost, 1);
        main.Children.Add(_pageHost);
        Grid.SetRow(main, 0);

        var footer = new Grid { Margin = new Thickness(20, 10, 56, 18) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var footerInfo = new StackPanel { Spacing = 0, Opacity = .68, Margin = new Thickness(8, 0, 0, 0) };
        footerInfo.Children.Add(new TextBlock { Text = T("FooterVersion") });
        var sourceLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        sourceLine.Children.Add(new TextBlock { Text = "sol669 ·", VerticalAlignment = VerticalAlignment.Center });
        sourceLine.Children.Add(new HyperlinkButton
        {
            Content = "GitHub",
            NavigateUri = new Uri("https://github.com/sol669/Room-Switcher-Tray"),
            Padding = new Thickness(0)
        });
        footerInfo.Children.Add(sourceLine);
        footer.Children.Add(footerInfo);

        _deleteButton = StyledButton(T("Delete"), "RoomDefaultButtonStyle", 110);
        _deleteButton.HorizontalAlignment = HorizontalAlignment.Left;
        _deleteButton.Margin = new Thickness(24, 0, 0, 0);
        _deleteButton.Click += async (_, _) => await DeleteScenarioAsync();
        Grid.SetColumn(_deleteButton, 1);
        footer.Children.Add(_deleteButton);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        Button close = StyledButton(T("Close"), "RoomDefaultButtonStyle", 110);
        close.Click += async (_, _) => await RequestCloseAsync();
        _saveButton = StyledButton(T("Save"), "RoomAccentButtonStyle", 110);
        _saveButton.Click += async (_, _) => await SaveCurrentPageAsync();
        actions.Children.Add(close);
        actions.Children.Add(_saveButton);
        Grid.SetColumn(actions, 2);
        footer.Children.Add(actions);
        Grid.SetRow(footer, 1);

        _root.Children.Clear();
        _root.Children.Add(main);
        _root.Children.Add(footer);
        _root.KeyDown -= RootKeyDown;
        _root.KeyDown += RootKeyDown;
        Content = _root;
        ShowCurrentPage();
        ApplyTheme();
        UpdateFooterState();
    }

    private UIElement BuildNavigation()
    {
        var navigation = new Grid { Margin = new Thickness(20, 20, 16, 12) };
        navigation.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        navigation.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var settingsGroup = new StackPanel { Spacing = 5 };
        settingsGroup.Children.Add(SettingsTitleCell(T("Settings")));
        settingsGroup.Children.Add(NavButton(T("General"), "general"));
        settingsGroup.Children.Add(NavButton(T("Devices"), "devices"));
        settingsGroup.Children.Add(NavigationSeparatorCell());
        navigation.Children.Add(settingsGroup);

        _scenarioNavPanel = new StackPanel { Spacing = 5, Margin = new Thickness(0, 5, 0, 0) };
        var scenarioScroll = new ScrollViewer
        {
            Content = _scenarioNavPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scenarioScroll, 1);
        navigation.Children.Add(scenarioScroll);
        RebuildScenarioNavigation();
        return navigation;
    }

    private static Border NavigationSeparatorCell() => new()
    {
        Height = 46,
        Child = new Border
        {
            Height = 1,
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)),
            Margin = new Thickness(8, 0, 8, 0)
        }
    };

    private static Border SettingsTitleCell(string text) => new()
    {
        Height = 97,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 30,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        }
    };

    private Button NavButton(string text, string page, bool nested = false)
    {
        var button = new Button
        {
            Content = text,
            Height = 46,
            Padding = new Thickness(14, 0, 14, 0),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = nested ? new Thickness(8, 0, 0, 0) : new Thickness(0)
        };
        _navButtons[page] = button;
        button.Click += async (_, _) => await RequestNavigateAsync(page);
        return button;
    }

    private void RebuildScenarioNavigation()
    {
        if (_scenarioNavPanel is null) return;
        foreach (string key in _navButtons.Keys.Where(key => key.StartsWith("scenario:", StringComparison.Ordinal)).ToList())
            _navButtons.Remove(key);
        _navButtons.Remove("new");
        _scenarioNavPanel.Children.Clear();
        foreach (ScenarioDefinition scenario in _scenarios)
        {
            string page = ScenarioPage(scenario.Id);
            _scenarioNavPanel.Children.Add(NavButton(scenario.Name, page, nested: true));
        }
        _scenarioNavPanel.Children.Add(NavButton("+ " + T("NewScenario"), "new", nested: true));
        RefreshNavigationSelection();
    }

    private async Task RequestNavigateAsync(string page)
    {
        if (page == _currentPage) return;
        if (!await ConfirmLeaveCurrentPageAsync()) return;
        NavigateCore(page);
    }

    private void NavigateCore(string page)
    {
        _currentPage = page;
        _capturingHotKey = false;
        if (page == "general") ResetPendingGeneral();
        else if (page == "devices") ResetPendingAliases();
        else if (page == "new") CreateNewDraft();
        else if (TryScenarioId(page, out Guid id)) LoadScenarioDraft(id);
        ShowCurrentPage();
        RefreshNavigationSelection();
        UpdateFooterState();
        _root.DispatcherQueue.TryEnqueue(RefreshNavigationSelection);
    }

    private void ShowCurrentPage()
    {
        _pageHost.Content = _currentPage switch
        {
            "devices" => BuildDevicesPage(),
            "new" => BuildScenarioPage(),
            _ when _currentPage.StartsWith("scenario:", StringComparison.Ordinal) => BuildScenarioPage(),
            _ => BuildGeneralPage()
        };
    }

    private void RefreshNavigationSelection()
    {
        foreach ((string page, Button button) in _navButtons)
        {
            bool selected = page == _currentPage;
            button.Background = selected ? AccentBrush() : new SolidColorBrush(Colors.Transparent);
            button.Foreground = selected ? AccentTextBrush() :
                ThemeBrush("TextFillColorPrimaryBrush", IsDark() ? Colors.White : Colors.Black);
        }
    }

    private UIElement BuildGeneralPage()
    {
        _loading = true;
        StackPanel panel = PagePanel();
        panel.Children.Add(TitleSpacerCell());
        panel.Children.Add(HeaderCell(T("Behavior")));
        _startupChoiceBox = SettingsComboBox(new ComboBox { DisplayMemberPath = "Name" });
        var startupChoices = new List<StartupChoice> { new(null, T("LastLoaded")) };
        startupChoices.AddRange(_scenarios.Where(s => s.IsComplete).Select(s => new StartupChoice(s.Id, s.Name)));
        _startupChoiceBox.ItemsSource = startupChoices;
        _startupChoiceBox.SelectedIndex = _pendingStartupMode == StartupScenarioMode.AlwaysUseScenario
            ? Math.Max(0, startupChoices.FindIndex(choice => choice.Id == _pendingStartupScenarioId)) : 0;
        _startupChoiceBox.SelectionChanged += (_, _) =>
        {
            if (_loading || _startupChoiceBox.SelectedItem is not StartupChoice choice) return;
            _pendingStartupMode = choice.Id is null ? StartupScenarioMode.RestoreLastScenario : StartupScenarioMode.AlwaysUseScenario;
            _pendingStartupScenarioId = choice.Id;
            UpdateFooterState();
        };
        panel.Children.Add(SettingRow(T("StartupScenario"), _startupChoiceBox));

        _hotKeyCaptureButton = SettingsButton(new Button { Content = T("Change"), MinWidth = 110 });
        _hotKeyCaptureButton.Click += (_, _) => BeginCapture();
        _hotKeyTitle = new TextBlock
        {
            Text = $"{T("Hotkey")} — {TrayService.FormatHotKey(_pendingHotKey)}",
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(SettingRow(_hotKeyTitle, _hotKeyCaptureButton));
        _hotKeyHint = new TextBlock { Opacity = .68, Margin = new Thickness(14, 0, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(_hotKeyHint);

        panel.Children.Add(HeaderCell(T("System")));

        _autostartToggle = new ToggleSwitch
        {
            IsOn = _pendingStartWithWindows,
            MinWidth = 0,
            Width = 44,
            OffContent = string.Empty,
            OnContent = string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0)
        };
        _autostartState = new TextBlock
        {
            MinWidth = 58,
            TextAlignment = TextAlignment.Right,
            Opacity = .72,
            VerticalAlignment = VerticalAlignment.Center
        };
        _autostartToggle.Toggled += (_, _) =>
        {
            UpdateAutostartState();
            if (_loading) return;
            _pendingStartWithWindows = _autostartToggle.IsOn;
            UpdateFooterState();
        };
        UpdateAutostartState();
        panel.Children.Add(ToggleSettingRow(T("Autostart"), _autostartState, _autostartToggle));

        ComboBox theme = SettingsComboBox(new ComboBox
        {
            ItemsSource = English ? new[] { "Like Windows", "Light", "Dark" } : new[] { "Как в Windows", "Светлая", "Тёмная" },
            SelectedIndex = (int)_pendingTheme
        });
        theme.SelectionChanged += (_, _) =>
        {
            if (!_loading && theme.SelectedIndex >= 0) { _pendingTheme = (AppThemeMode)theme.SelectedIndex; UpdateFooterState(); }
        };
        panel.Children.Add(SettingRow(T("Theme"), theme));

        ComboBox language = SettingsComboBox(new ComboBox
        {
            ItemsSource = new[] { "Русский", "English" },
            SelectedIndex = (int)_pendingLanguage
        });
        language.SelectionChanged += (_, _) =>
        {
            if (!_loading && language.SelectedIndex >= 0) { _pendingLanguage = (AppLanguage)language.SelectedIndex; UpdateFooterState(); }
        };
        panel.Children.Add(SettingRow(T("Language"), language));
        FinishInitialLoading(panel);
        return PageScroll(panel);
    }

    private UIElement BuildDevicesPage()
    {
        _loading = true;
        StackPanel panel = PagePanel();
        panel.Children.Add(TitleSpacerCell());
        AddDeviceAliasRows(panel, T("Monitors"), _displays.Select(display => (display.Id, display.Name)));
        AddDeviceAliasRows(panel, T("AudioDevices"), _audio.Select(audio => (audio.Id, audio.DisplayName ?? audio.Name)));
        FinishInitialLoading(panel);
        return PageScroll(panel);
    }

    private UIElement BuildScenarioPage()
    {
        _loading = true;
        StackPanel panel = PagePanel();
        panel.Children.Add(TitleSpacerCell());
        if (_draft is null)
        {
            FinishInitialLoading(panel);
            return PageScroll(panel);
        }

        panel.Children.Add(HeaderCell(T("ScenarioSettings")));
        TextBox name = SettingsTextBox(new TextBox { Text = _draft.Name, MaxLength = 80 });
        name.TextChanged += (_, _) =>
        {
            if (_loading || _draft is null) return;
            _draft.Name = name.Text;
            UpdateFooterState();
        };
        panel.Children.Add(SettingRow(T("ScenarioName"), name));

        panel.Children.Add(HeaderCell(T("Monitors")));

        for (int index = 0; index < 4; index++)
        {
            int slot = index;
            ComboBox monitor = SettingsComboBox(new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id" });
            monitor.Tag = slot;
            BindMonitorBox(monitor, slot);
            monitor.SelectionChanged += (_, _) =>
            {
                if (_loading) return;
                _draftMonitorSlots[slot] = monitor.SelectedValue as string ?? string.Empty;
                CaptureMonitorSlots();
                RefreshScenarioPagePreservingFocus();
            };
            panel.Children.Add(SettingRow($"{T("Monitor")} {index + 1}", monitor));
        }

        ComboBox audio = SettingsComboBox(new ComboBox { DisplayMemberPath = "Name", SelectedValuePath = "Id" });
        var audioChoices = new List<Choice> { new(string.Empty, T("None")) };
        audioChoices.AddRange(_audio.Select(device => new Choice(device.Id,
            DeviceAliasService.NameFor(PendingAliasSettings(), device.Id, device.DisplayName ?? device.Name))));
        audio.ItemsSource = audioChoices;
        audio.SelectedValue = _draft.AudioDeviceId;
        audio.SelectionChanged += (_, _) =>
        {
            if (_loading || _draft is null) return;
            _draft.AudioDeviceId = audio.SelectedValue as string ?? string.Empty;
            _draft.AudioDeviceContainerId = _audio.FirstOrDefault(item => item.Id == _draft.AudioDeviceId)
                ?.ContainerId?.ToString("D") ?? string.Empty;
            UpdateFooterState();
        };
        panel.Children.Add(SettingRow(T("Audio"), audio));

        ComboBox volume = SettingsComboBox(new ComboBox
        {
            ItemsSource = VolumeChoices(),
            DisplayMemberPath = "Name",
            SelectedValuePath = "Value",
            SelectedValue = _draft.VolumePercent
        });
        volume.SelectionChanged += (_, _) =>
        {
            if (!_loading && _draft is not null) { _draft.VolumePercent = volume.SelectedValue as int?; UpdateFooterState(); }
        };
        panel.Children.Insert(panel.Children.Count - 1, HeaderCell(T("Sound")));
        panel.Children.Add(SettingRow(T("Volume"), volume));

        panel.Children.Add(HeaderCell(T("TrayIcon")));

        List<IconChoice> iconChoices = IconChoices().ToList();
        ComboBox icon = SettingsComboBox(new ComboBox
        {
            ItemsSource = iconChoices,
            DisplayMemberPath = "Name",
            SelectedIndex = Math.Clamp((int)_draft.Icon, 0, iconChoices.Count - 1)
        });
        icon.SelectionChanged += (_, _) =>
        {
            if (_loading || _draft is null || icon.SelectedItem is not IconChoice selected) return;
            _draft.Icon = selected.Value;
            ShowCurrentPage();
            UpdateFooterState();
        };
        panel.Children.Add(SettingRow(T("ScenarioIcon"), icon));

        if (_draft.Icon == ScenarioIcon.Letters)
        {
            TextBox letters = SettingsTextBox(new TextBox { Text = _draft.IconLetters, MaxLength = 2 });
            letters.TextChanged += (_, _) =>
            {
                if (_loading || _draft is null) return;
                string normalized = ScenarioDefinition.MakeIconLetters(letters.Text);
                if (!string.Equals(letters.Text, normalized, StringComparison.Ordinal))
                {
                    int position = normalized.Length;
                    letters.Text = normalized;
                    letters.SelectionStart = position;
                }
                _draft.IconLetters = normalized;
                UpdateFooterState();
            };
            panel.Children.Add(SettingRow(T("Letters"), letters));
        }

        FinishInitialLoading(panel);
        return PageScroll(panel);
    }

    private void BindMonitorBox(ComboBox box, int slot)
    {
        string own = _draftMonitorSlots[slot] ?? string.Empty;
        HashSet<string> used = _draftMonitorSlots
            .Where((id, index) => index != slot && !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var choices = new List<Choice> { new(string.Empty, T("None")) };
        choices.AddRange(_displays
            .Where(device => device.Id.Equals(own, StringComparison.OrdinalIgnoreCase) || !used.Contains(device.Id))
            .Select(device => new Choice(device.Id,
                DeviceAliasService.NameFor(PendingAliasSettings(), device.Id, device.Name))));
        box.ItemsSource = choices;
        box.SelectedValue = own;
    }

    private void CaptureMonitorSlots()
    {
        if (_draft is null) return;
        _draft.DisplayIds = _draftMonitorSlots
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void RefreshScenarioPagePreservingFocus()
    {
        ShowCurrentPage();
        UpdateFooterState();
    }

    private void AddDeviceAliasRows(StackPanel panel, string heading, IEnumerable<(string Id, string SystemName)> devices)
    {
        List<(string Id, string SystemName)> items = devices
            .Where(device => !string.IsNullOrWhiteSpace(device.Id))
            .GroupBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        panel.Children.Add(HeaderCell(heading));
        if (items.Count == 0) return;
        foreach ((string id, string systemName) in items)
        {
            TextBox alias = SettingsTextBox(new TextBox
            {
                Text = _pendingAliases.TryGetValue(id, out string? value) ? value : string.Empty,
                PlaceholderText = T("DeviceAlias")
            });
            alias.TextChanged += (_, _) =>
            {
                if (_loading) return;
                string value = alias.Text.Trim();
                if (string.IsNullOrWhiteSpace(value)) _pendingAliases.Remove(id);
                else _pendingAliases[id] = value;
                UpdateFooterState();
            };
            var title = new TextBlock
            {
                Text = systemName,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTipService.SetToolTip(title, systemName);
            panel.Children.Add(SettingRow(title, alias));
        }
    }

    private static StackPanel PagePanel() => new()
    {
        Width = 540,
        HorizontalAlignment = HorizontalAlignment.Left,
        Spacing = 5,
        Margin = new Thickness(24, 20, 0, 12)
    };

    private static ScrollViewer PageScroll(UIElement content) => new()
    {
        Content = content,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Padding = new Thickness(0)
    };

    private static Border HeaderCell(string text)
    {
        var cell = new Border { Height = 46 };
        if (!string.IsNullOrWhiteSpace(text))
        {
            cell.Child = new TextBlock
            {
                Text = text,
                Opacity = .52,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 0, 0)
            };
        }
        return cell;
    }

    private static Border TitleSpacerCell() => new() { Height = 97 };

    private void FinishInitialLoading(FrameworkElement element)
    {
        element.Loaded += (_, _) =>
        {
            _loading = false;
            UpdateFooterState();
        };
    }

    private ComboBox SettingsComboBox(ComboBox comboBox)
    {
        comboBox.Width = 244;
        comboBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        ApplyControlStyle(comboBox, "RoomSettingsComboBoxStyle");
        return comboBox;
    }

    private TextBox SettingsTextBox(TextBox textBox)
    {
        textBox.Width = 244;
        textBox.HorizontalAlignment = HorizontalAlignment.Stretch;
        ApplyControlStyle(textBox, "RoomDeviceAliasTextBoxStyle");
        return textBox;
    }

    private Button SettingsButton(Button button)
    {
        ApplyControlStyle(button, "RoomHotkeyButtonStyle");
        return button;
    }

    private static Button StyledButton(string text, string style, double minWidth)
    {
        var button = new Button { Content = text, MinWidth = minWidth };
        ApplyControlStyle(button, style);
        return button;
    }

    private Border SettingRow(string title, FrameworkElement control) =>
        SettingRow(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center }, control);

    private Border SettingRow(FrameworkElement title, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(title);
        Grid.SetColumn(control, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(control);
        return SettingsCard(grid);
    }

    private Border ToggleSettingRow(string title, TextBlock state, ToggleSwitch toggle)
    {
        state.HorizontalAlignment = HorizontalAlignment.Right;
        state.VerticalAlignment = VerticalAlignment.Center;
        toggle.HorizontalAlignment = HorizontalAlignment.Right;
        toggle.VerticalAlignment = VerticalAlignment.Center;

        var right = new Grid { Width = 244, Height = 34, ColumnSpacing = 12 };
        right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        right.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        right.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(state, 1);
        Grid.SetColumn(toggle, 2);
        right.Children.Add(state);
        right.Children.Add(toggle);
        return SettingRow(title, right);
    }

    private static Border SettingsCard(Grid content)
    {
        var card = new Border { Child = content };
        ApplyControlStyle(card, "RoomSettingsCardStyle");
        return card;
    }

    private static void ApplyControlStyle(FrameworkElement control, string resourceKey)
    {
        if (Application.Current.Resources[resourceKey] is Style style) control.Style = style;
    }

    private void UpdateAutostartState()
    {
        if (_autostartState is null || _autostartToggle is null) return;
        _autostartState.Text = _autostartToggle.IsOn ? (English ? "On" : "Вкл.") : (English ? "Off" : "Откл.");
    }

    private void UpdateFooterState()
    {
        if (_saveButton is not null)
            _saveButton.IsEnabled = !_loading && CanSaveCurrentPage() && HasCurrentChanges();
        if (_deleteButton is not null)
            _deleteButton.Visibility = IsScenarioPage && !_draftIsNew && _scenarios.Count > 1
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool CanSaveCurrentPage() => !IsScenarioPage || _draft?.IsComplete == true;

    private bool HasCurrentChanges() => _currentPage switch
    {
        "general" => HasUnsavedGeneralChanges(),
        "devices" => HasUnsavedAliasChanges(),
        _ when IsScenarioPage => _draft is not null && (_draftIsNew || !ScenarioEquals(_draft, _scenarioBaseline)),
        _ => false
    };

    private bool IsScenarioPage => _currentPage == "new" || _currentPage.StartsWith("scenario:", StringComparison.Ordinal);

    private async Task<bool> SaveCurrentPageAsync()
    {
        if (!CanSaveCurrentPage()) return false;
        if (_currentPage == "general") return SaveGeneral();
        if (_currentPage == "devices") return SaveAliases();
        if (IsScenarioPage) return await SaveScenarioAsync();
        return false;
    }

    private bool SaveGeneral()
    {
        try
        {
            bool appearanceChanged = _pendingTheme != _settings.Current.Theme ||
                _pendingLanguage != _settings.Current.Language;
            StartupService.SetEnabled(_pendingStartWithWindows);
            _settings.Current.StartWithWindows = _pendingStartWithWindows;
            _settings.Current.StartupScenarioMode = _pendingStartupMode;
            _settings.Current.StartupScenarioId = _pendingStartupScenarioId;
            _settings.Current.Theme = _pendingTheme;
            _settings.Current.Language = _pendingLanguage;
            _settings.Current.SwitchScenarioHotKey = CloneHotKey(_pendingHotKey);
            _settings.Save();
            ResetPendingGeneral();
            _tray.Refresh();
            if (appearanceChanged) BuildShell();
            else { ApplyTheme(); RefreshNavigationSelection(); UpdateFooterState(); }
            return true;
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            UpdateFooterState();
            return false;
        }
    }

    private bool SaveAliases()
    {
        try
        {
            _settings.Current.DeviceAliases = new Dictionary<string, string>(_pendingAliases, StringComparer.OrdinalIgnoreCase);
            _settings.Save();
            ResetPendingAliases();
            _tray.Refresh();
            UpdateFooterState();
            return true;
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            UpdateFooterState();
            return false;
        }
    }

    private async Task<bool> SaveScenarioAsync()
    {
        if (_draft?.IsComplete != true) return false;
        _draft.Name = _draft.Name.Trim();
        _draft.IconLetters = ScenarioDefinition.MakeIconLetters(_draft.IconLetters);
        bool wasActive = !_draftIsNew && _settings.Current.ActiveScenarioId == _draft.Id;
        bool operationalChanged = wasActive && !OperationallyEqual(_draft, _scenarioBaseline);

        if (_draftIsNew)
        {
            _scenarios.Add(_draft.Clone());
            _draftIsNew = false;
        }
        else
        {
            int index = _scenarios.FindIndex(item => item.Id == _draft.Id);
            if (index >= 0) _scenarios[index] = _draft.Clone();
        }
        _settings.Current.Scenarios = _scenarios.Select(item => item.Clone()).ToList();
        _settings.Save();
        _scenarioBaseline = _draft.Clone();
        _currentPage = ScenarioPage(_draft.Id);
        _tray.Refresh();
        RebuildScenarioNavigation();
        RefreshNavigationSelection();
        UpdateFooterState();

        if (operationalChanged) await PromptApplyScenarioAsync(_draft);
        return true;
    }

    private async Task PromptApplyScenarioAsync(ScenarioDefinition scenario)
    {
        if (_root.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = English ? $"Scenario “{scenario.Name}” saved" : $"Сценарий «{scenario.Name}» сохранён",
            Content = English ? "Apply the updated scenario now?" : "Применить обновлённый сценарий сейчас?",
            PrimaryButtonText = English ? "Apply now" : "Применить сейчас",
            CloseButtonText = English ? "Later" : "Позже",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await _tray.ApplyScenarioAsync(scenario.Id);
    }

    private async Task DeleteScenarioAsync()
    {
        if (_draft is null || _draftIsNew || _scenarios.Count <= 1 || _root.XamlRoot is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = English ? $"Delete “{_draft.Name}”?" : $"Удалить сценарий «{_draft.Name}»?",
            Content = English ? "This action cannot be undone." : "Это действие нельзя отменить.",
            PrimaryButtonText = T("Delete"),
            CloseButtonText = English ? "Cancel" : "Отмена",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        Guid deletedId = _draft.Id;
        _scenarios.RemoveAll(item => item.Id == deletedId);
        _settings.Current.Scenarios = _scenarios.Select(item => item.Clone()).ToList();
        if (_settings.Current.ActiveScenarioId == deletedId) _settings.Current.ActiveScenarioId = null;
        if (_settings.Current.StartupScenarioId == deletedId)
        {
            _settings.Current.StartupScenarioId = null;
            _settings.Current.StartupScenarioMode = StartupScenarioMode.RestoreLastScenario;
        }
        _settings.Save();
        _tray.Refresh();
        _draft = null;
        _scenarioBaseline = null;
        _currentPage = "general";
        ResetPendingGeneral();
        RebuildScenarioNavigation();
        ShowCurrentPage();
        RefreshNavigationSelection();
        UpdateFooterState();
    }

    private async Task RequestCloseAsync()
    {
        if (HasCurrentChanges()) { await ConfirmCloseAsync(); return; }
        _allowClose = true;
        Close();
    }

    private async Task ConfirmCloseAsync()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        try
        {
            if (!await ConfirmLeaveCurrentPageAsync()) return;
            _allowClose = true;
            Close();
        }
        finally { _dialogOpen = false; }
    }

    private async Task<bool> ConfirmLeaveCurrentPageAsync()
    {
        if (!HasCurrentChanges()) return true;
        if (_root.XamlRoot is null) return false;

        if (IsScenarioPage && _draft?.IsComplete != true)
        {
            var invalid = new ContentDialog
            {
                XamlRoot = _root.XamlRoot,
                Title = _draftIsNew
                    ? (English ? "Cancel scenario creation?" : "Отменить создание сценария?")
                    : (English ? "Discard scenario changes?" : "Отменить изменения сценария?"),
                Content = English ? "The changes you made will be lost." : "Внесённые изменения будут потеряны.",
                PrimaryButtonText = _draftIsNew
                    ? (English ? "Cancel creation" : "Отменить создание")
                    : (English ? "Discard changes" : "Отменить изменения"),
                CloseButtonText = English ? "Continue editing" : "Продолжить редактирование",
                DefaultButton = ContentDialogButton.Close
            };
            if (await invalid.ShowAsync() != ContentDialogResult.Primary) return false;
            ResetCurrentPageChanges();
            return true;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = _root.XamlRoot,
            Title = English ? "Save changes?" : "Сохранить изменения?",
            Content = English ? "There are unsaved changes." : "Остались несохранённые изменения.",
            PrimaryButtonText = T("Save"),
            SecondaryButtonText = English ? "Don't save" : "Не сохранять",
            CloseButtonText = English ? "Cancel" : "Отмена",
            DefaultButton = ContentDialogButton.Primary
        };
        ContentDialogResult result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary) return await SaveCurrentPageAsync();
        if (result == ContentDialogResult.Secondary)
        {
            ResetCurrentPageChanges();
            return true;
        }
        return false;
    }

    private void ResetCurrentPageChanges()
    {
        if (_currentPage == "general")
        {
            _tray.TryUpdateHotKey(_settings.Current.SwitchScenarioHotKey, out _);
            ResetPendingGeneral();
        }
        else if (_currentPage == "devices") ResetPendingAliases();
        else if (_draftIsNew) { _draft = null; _scenarioBaseline = null; }
        else if (_scenarioBaseline is not null)
        {
            _draft = _scenarioBaseline.Clone();
            SetMonitorSlots(_draft);
        }
    }

    private void BeginCapture()
    {
        _capturingHotKey = true;
        if (_hotKeyCaptureButton is not null)
            _hotKeyCaptureButton.Content = English ? "Press shortcut…" : "Нажмите сочетание…";
        if (_hotKeyHint is not null)
        {
            _hotKeyHint.Text = English
                ? "Use Ctrl, Alt, Shift or Win with another key."
                : "Нажмите Ctrl, Alt, Shift или Win вместе с другой клавишей.";
            _hotKeyHint.Visibility = Visibility.Visible;
        }
        _root.Focus(FocusState.Programmatic);
    }

    private void RootKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_capturingHotKey || args.Key is VirtualKey.Control or VirtualKey.Shift or VirtualKey.Menu or VirtualKey.LeftWindows or VirtualKey.RightWindows) return;
        uint modifiers = 0;
        if (Down(VirtualKey.Control)) modifiers |= HotKeyDefinition.Control;
        if (Down(VirtualKey.Menu)) modifiers |= HotKeyDefinition.Alt;
        if (Down(VirtualKey.Shift)) modifiers |= HotKeyDefinition.Shift;
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) modifiers |= HotKeyDefinition.Win;
        var hotKey = new HotKeyDefinition { Modifiers = modifiers, VirtualKey = (uint)args.Key };
        _capturingHotKey = false;
        if (_tray.TryUpdateHotKey(hotKey, out string error))
        {
            _pendingHotKey = hotKey;
            if (_hotKeyTitle is not null) _hotKeyTitle.Text = $"{T("Hotkey")} — {TrayService.FormatHotKey(hotKey)}";
            if (_hotKeyHint is not null)
                _hotKeyHint.Text = English ? "Shortcut changed. Click Save." : "Сочетание изменено. Нажмите «Сохранить».";
        }
        else if (_hotKeyHint is not null) _hotKeyHint.Text = error;
        if (_hotKeyCaptureButton is not null) _hotKeyCaptureButton.Content = T("Change");
        UpdateFooterState();
        args.Handled = true;
    }

    private static bool Down(VirtualKey key) => (GetKeyState((int)key) & unchecked((short)0x8000)) != 0;

    private void CreateNewDraft()
    {
        if (_draftIsNew && _draft is not null) return;
        string name = NextScenarioName();
        _draft = new ScenarioDefinition
        {
            Name = name,
            Icon = ScenarioIcon.Letters,
            IconLetters = ScenarioDefinition.MakeIconLetters(name),
            VolumePercent = null
        };
        if (_scenarios.Count == 0)
        {
            _draft.DisplayIds = _displays.Where(display => display.IsActive).Take(4).Select(display => display.Id).ToList();
            AudioDevice? currentAudio = _audio.FirstOrDefault(device => device.IsDefault);
            if (currentAudio is not null)
            {
                _draft.AudioDeviceId = currentAudio.Id;
                _draft.AudioDeviceContainerId = currentAudio.ContainerId?.ToString("D") ?? string.Empty;
            }
        }
        _scenarioBaseline = null;
        _draftIsNew = true;
        SetMonitorSlots(_draft);
    }

    private void LoadScenarioDraft(Guid id)
    {
        ScenarioDefinition? scenario = _scenarios.FirstOrDefault(item => item.Id == id);
        _draft = scenario?.Clone();
        _scenarioBaseline = scenario?.Clone();
        _draftIsNew = false;
        SetMonitorSlots(_draft);
    }

    private void SetMonitorSlots(ScenarioDefinition? scenario)
    {
        _draftMonitorSlots = new string[4];
        if (scenario is null) return;
        for (int index = 0; index < Math.Min(4, scenario.DisplayIds.Count); index++)
            _draftMonitorSlots[index] = scenario.DisplayIds[index];
    }

    private string NextScenarioName()
    {
        string root = English ? "New scenario" : "Новый сценарий";
        var names = _scenarios.Select(item => item.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        for (int index = 1; ; index++)
        {
            string candidate = $"{root} {index}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private async Task OpenNewScenarioDraftAsync()
    {
        if (_currentPage == "new" && _draft is not null) return;
        if (!_devicesLoaded) await LoadDevicesAsync(false);
        await RequestNavigateAsync("new");
    }

    private async Task LoadDevicesAsync(bool openNewScenario)
    {
        if (!_devicesLoaded)
        {
            try
            {
                _displays.Clear();
                _displays.AddRange(await Task.Run(App.Displays.GetDisplays));
                _audio.Clear();
                _audio.AddRange(await App.Audio.GetVisibleRenderDevicesAsync(_displays, _scenarios.Cast<ScenarioDefinition?>().ToArray()));
                _devicesLoaded = true;
                if (_currentPage is "devices" || _currentPage == "new" || _currentPage.StartsWith("scenario:", StringComparison.Ordinal))
                    ShowCurrentPage();
            }
            catch (Exception ex) { SettingsStore.Log(ex); }
        }
        if (openNewScenario) await RequestNavigateAsync("new");
    }

    private void ResetPendingGeneral()
    {
        _pendingStartWithWindows = _settings.Current.StartWithWindows;
        _pendingStartupMode = _settings.Current.StartupScenarioMode;
        _pendingStartupScenarioId = _settings.Current.StartupScenarioId;
        _pendingTheme = _settings.Current.Theme;
        _pendingLanguage = _settings.Current.Language;
        _pendingHotKey = CloneHotKey(_settings.Current.SwitchScenarioHotKey);
    }

    private void ResetPendingAliases()
    {
        _pendingAliases.Clear();
        foreach ((string id, string alias) in _settings.Current.DeviceAliases)
            if (!string.IsNullOrWhiteSpace(alias)) _pendingAliases[id] = alias.Trim();
    }

    private bool HasUnsavedGeneralChanges()
    {
        AppSettings current = _settings.Current;
        return _pendingStartWithWindows != current.StartWithWindows ||
            _pendingStartupMode != current.StartupScenarioMode ||
            _pendingStartupScenarioId != current.StartupScenarioId ||
            _pendingTheme != current.Theme ||
            _pendingLanguage != current.Language ||
            _pendingHotKey.Modifiers != current.SwitchScenarioHotKey.Modifiers ||
            _pendingHotKey.VirtualKey != current.SwitchScenarioHotKey.VirtualKey;
    }

    private bool HasUnsavedAliasChanges()
    {
        Dictionary<string, string> saved = _settings.Current.DeviceAliases
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.OrdinalIgnoreCase);
        return saved.Count != _pendingAliases.Count || saved.Any(pair =>
            !_pendingAliases.TryGetValue(pair.Key, out string? alias) || !string.Equals(pair.Value, alias, StringComparison.Ordinal));
    }

    private AppSettings PendingAliasSettings() => new()
    {
        DeviceAliases = new Dictionary<string, string>(_pendingAliases, StringComparer.OrdinalIgnoreCase)
    };

    private static HotKeyDefinition CloneHotKey(HotKeyDefinition? hotKey)
    {
        hotKey ??= HotKeyDefinition.Default;
        return new HotKeyDefinition { Modifiers = hotKey.Modifiers, VirtualKey = hotKey.VirtualKey };
    }

    private static bool ScenarioEquals(ScenarioDefinition left, ScenarioDefinition? right) => right is not null &&
        left.Id == right.Id &&
        string.Equals(left.Name.Trim(), right.Name.Trim(), StringComparison.Ordinal) &&
        left.DisplayIds.SequenceEqual(right.DisplayIds, StringComparer.OrdinalIgnoreCase) &&
        string.Equals(left.AudioDeviceId, right.AudioDeviceId, StringComparison.OrdinalIgnoreCase) &&
        left.VolumePercent == right.VolumePercent &&
        left.Icon == right.Icon &&
        string.Equals(ScenarioDefinition.MakeIconLetters(left.IconLetters),
            ScenarioDefinition.MakeIconLetters(right.IconLetters), StringComparison.Ordinal);

    private static bool OperationallyEqual(ScenarioDefinition left, ScenarioDefinition? right) => right is not null &&
        left.DisplayIds.SequenceEqual(right.DisplayIds, StringComparer.OrdinalIgnoreCase) &&
        string.Equals(left.AudioDeviceId, right.AudioDeviceId, StringComparison.OrdinalIgnoreCase) &&
        left.VolumePercent == right.VolumePercent;

    private IEnumerable<IconChoice> IconChoices() =>
    [
        new(ScenarioIcon.Letters, T("Letters")),
        new(ScenarioIcon.Desktop, T("Desktop")),
        new(ScenarioIcon.Television, T("Television")),
        new(ScenarioIcon.Sofa, T("Sofa")),
        new(ScenarioIcon.Gamepad, T("Gamepad"))
    ];

    private IEnumerable<IntChoice> VolumeChoices() =>
        new int?[] { null, 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100 }
            .Select(value => new IntChoice(value, value switch
            {
                null => T("NoChange"),
                0 => "0% — mute",
                int percent => $"{percent}%"
            }));

    private void ApplyTheme()
    {
        _root.RequestedTheme = _settings.Current.Theme switch
        {
            AppThemeMode.Light => ElementTheme.Light,
            AppThemeMode.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        NativeTheme.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), _settings.Current.Theme);
    }

    private bool IsDark() => _settings.Current.Theme == AppThemeMode.Dark ||
        (_settings.Current.Theme == AppThemeMode.System && NativeTheme.IsSystemDark());

    private SolidColorBrush ThemeBrush(string key, Color fallback)
    {
        if (Application.Current.Resources.TryGetValue(key, out object? resource))
        {
            if (resource is SolidColorBrush brush) return brush;
            if (resource is Color color) return new SolidColorBrush(color);
        }
        return new SolidColorBrush(fallback);
    }

    private SolidColorBrush AccentBrush() => ThemeBrush(
        "AccentFillColorDefaultBrush", new UISettings().GetColorValue(UIColorType.Accent));

    private SolidColorBrush AccentTextBrush()
    {
        Color accent = new UISettings().GetColorValue(UIColorType.Accent);
        double luminance = .299 * accent.R + .587 * accent.G + .114 * accent.B;
        return ThemeBrush("TextOnAccentFillColorPrimaryBrush", luminance > 165 ? Colors.Black : Colors.White);
    }

    private static string ScenarioPage(Guid id) => $"scenario:{id:D}";

    private static bool TryScenarioId(string page, out Guid id)
    {
        id = Guid.Empty;
        return page.StartsWith("scenario:", StringComparison.Ordinal) && Guid.TryParse(page[9..], out id);
    }

    private sealed record Choice(string Id, string Name);
    private sealed record IntChoice(int? Value, string Name);
    private sealed record IconChoice(ScenarioIcon Value, string Name);
    private sealed record StartupChoice(Guid? Id, string Name);
}
