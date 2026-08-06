using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoomSwitcherTray.Models;
using RoomSwitcherTray.Services;
using System.Runtime.InteropServices;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace RoomSwitcherTray;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly TrayService _tray;
    private IReadOnlyList<DisplayDevice> _displays = [];
    private IReadOnlyList<AudioDevice> _audioDevices = [];
    private Scenario? _editing;
    private bool _isNew;
    // XAML can raise SelectionChanged while InitializeComponent is still
    // constructing ComboBox items. Ignore every UI event until the window and
    // its dependencies are fully initialized.
    private bool _loading = true;
    private ComboBox IconCombo = null!;
    private TextBlock IconLabel = null!;
    private Button DevicesButton = null!;
    private ToggleSwitch AutostartToggle = null!;

    public SettingsWindow(SettingsStore store, TrayService tray)
    {
        _store = store;
        _tray = tray;
        BuildLayout();
        ConfigureWindow();
        LoadDevices();
        ReloadScenarioList();
        _loading = true;
        ThemeCombo.SelectedIndex = (int)_store.Current.Theme;
        LanguageCombo.SelectedIndex = _store.Current.Language == AppLanguage.Russian ? 0 : 1;
        ApplyTheme();
        ApplyLanguage();
        _loading = false;

        if (_store.Current.Scenarios.Count > 0)
            ScenarioList.SelectedIndex = 0;
        else
            BeginCreate();
    }

    private void BuildLayout()
    {
        Title = "Room Switcher Tray";

        RootGrid = new Grid
        {
            Padding = new Thickness(20),
            RowSpacing = 12
        };
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var contentGrid = new Grid { ColumnSpacing = 22 };
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var scenarioGrid = new Grid();
        scenarioGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        scenarioGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        scenarioGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        scenarioGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        CreateButton = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = "+ Create scenario"
        };
        CreateButton.Click += CreateButton_Click;
        scenarioGrid.Children.Add(CreateButton);

        ScenarioList = new ListView { Margin = new Thickness(0, 12, 0, 0) };
        ScenarioList.SelectionChanged += ScenarioList_SelectionChanged;
        Grid.SetRow(ScenarioList, 1);
        scenarioGrid.Children.Add(ScenarioList);
        DevicesButton = new Button
        {
            Content = "Devices",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DevicesButton.Click += DevicesButton_Click;
        Grid.SetRow(DevicesButton, 2);
        scenarioGrid.Children.Add(DevicesButton);
        AutostartToggle = new ToggleSwitch
        {
            Header = "Start with Windows",
            IsOn = _store.Current.StartWithWindows,
            Margin = new Thickness(0, 14, 0, 0)
        };
        AutostartToggle.Toggled += (_, _) =>
        {
            if (_loading) return;
            _store.Current.StartWithWindows = AutostartToggle.IsOn;
            _store.Save();
        };
        Grid.SetRow(AutostartToggle, 3);
        scenarioGrid.Children.Add(AutostartToggle);
        contentGrid.Children.Add(scenarioGrid);

        EditorPanel = new StackPanel { Spacing = 10 };
        EditorTitle = new TextBlock
        {
            Text = "Scenario editor",
            FontSize = 24,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
        EditorPanel.Children.Add(EditorTitle);

        InfoMessage = new InfoBar { IsOpen = false, IsClosable = true };
        EditorPanel.Children.Add(InfoMessage);

        NameLabel = AddLabel(EditorPanel, "Name");
        NameBox = new TextBox();
        EditorPanel.Children.Add(NameBox);

        IconLabel = AddLabel(EditorPanel, "Scenario icon", true);
        IconCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        EditorPanel.Children.Add(IconCombo);

        DisplaysLabel = AddLabel(EditorPanel, "Displays", true);
        DisplaysPanel = new StackPanel { Spacing = 4 };
        EditorPanel.Children.Add(DisplaysPanel);

        PrimaryLabel = AddLabel(EditorPanel, "Primary display", true);
        PrimaryCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        EditorPanel.Children.Add(PrimaryCombo);

        AudioLabel = AddLabel(EditorPanel, "Audio", true);
        AudioCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        EditorPanel.Children.Add(AudioCombo);

        var actionPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Margin = new Thickness(0, 10, 0, 0)
        };
        SaveButton = new Button { Content = "Save" };
        SaveButton.Click += SaveButton_Click;
        actionPanel.Children.Add(SaveButton);
        SaveApplyButton = new Button { Content = "Save and apply" };
        SaveApplyButton.Click += SaveApplyButton_Click;
        actionPanel.Children.Add(SaveApplyButton);
        CancelButton = new Button { Content = "Cancel" };
        CancelButton.Click += CancelButton_Click;
        actionPanel.Children.Add(CancelButton);
        EditorPanel.Children.Add(actionPanel);

        DeleteButton = new Button
        {
            Content = "Delete scenario",
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        DeleteButton.Click += DeleteButton_Click;
        EditorPanel.Children.Add(DeleteButton);

        var editorScroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = EditorPanel
        };
        Grid.SetColumn(editorScroll, 1);
        contentGrid.Children.Add(editorScroll);
        RootGrid.Children.Add(contentGrid);

        var footer = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12)
        };
        var footerGrid = new Grid { ColumnSpacing = 12 };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var aboutPanel = new StackPanel();
        aboutPanel.Children.Add(new TextBlock
        {
            Text = "Room Switcher Tray 0.2.0",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        var authorPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        AuthorText = new TextBlock { Text = "Author: sol669 ·", Opacity = 0.68 };
        authorPanel.Children.Add(AuthorText);
        authorPanel.Children.Add(new HyperlinkButton
        {
            Content = "GitHub",
            Padding = new Thickness(0),
            NavigateUri = new Uri("https://github.com/sol669/Room-Switcher-Tray")
        });
        aboutPanel.Children.Add(authorPanel);
        footerGrid.Children.Add(aboutPanel);

        ThemeCombo = new ComboBox { Width = 135 };
        SystemThemeItem = new ComboBoxItem { Content = "System" };
        LightThemeItem = new ComboBoxItem { Content = "Light" };
        DarkThemeItem = new ComboBoxItem { Content = "Dark" };
        ThemeCombo.Items.Add(SystemThemeItem);
        ThemeCombo.Items.Add(LightThemeItem);
        ThemeCombo.Items.Add(DarkThemeItem);
        ThemeCombo.SelectionChanged += ThemeCombo_SelectionChanged;
        Grid.SetColumn(ThemeCombo, 1);
        footerGrid.Children.Add(ThemeCombo);

        LanguageCombo = new ComboBox { Width = 110 };
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "Русский" });
        LanguageCombo.Items.Add(new ComboBoxItem { Content = "English" });
        LanguageCombo.SelectionChanged += LanguageCombo_SelectionChanged;
        Grid.SetColumn(LanguageCombo, 2);
        footerGrid.Children.Add(LanguageCombo);

        footer.Child = footerGrid;
        Grid.SetRow(footer, 1);
        RootGrid.Children.Add(footer);
        Content = RootGrid;
    }

    private static TextBlock AddLabel(StackPanel panel, string text, bool topMargin = false)
    {
        var label = new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = topMargin ? new Thickness(0, 6, 0, 0) : new Thickness(0)
        };
        panel.Children.Add(label);
        return label;
    }

    private void ConfigureWindow()
    {
        nint hwnd = WindowNative.GetWindowHandle(this);
        WindowId id = Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Resize(new SizeInt32(850, 650));
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
        }
        NativeTheme.Apply(_store.Current.Theme, hwnd);
    }

    private void LoadDevices()
    {
        try
        {
            _displays = App.Displays.GetDisplays()
                .Select(d => d with { Name = _store.DisplayName(d) }).ToList();
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            _displays = [];
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }

        try
        {
            _audioDevices = App.Audio.GetRenderDevices()
                .Select(d => d with { Name = _store.AudioName(d) }).ToList();
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            _audioDevices = [];
            ShowInfo(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ReloadScenarioList()
    {
        _loading = true;
        Guid? selectedId = _editing?.Id;
        ScenarioList.ItemsSource = null;
        ScenarioList.ItemsSource = _store.Current.Scenarios;
        if (selectedId is Guid id)
            ScenarioList.SelectedItem = _store.Current.Scenarios.FirstOrDefault(s => s.Id == id);
        _loading = false;
    }

    private void EditScenario(Scenario scenario)
    {
        _editing = scenario.Clone();
        _isNew = false;
        PopulateEditor();
    }

    private void BeginCreate()
    {
        _editing = new Scenario();
        _isNew = true;
        ScenarioList.SelectedItem = null;
        PopulateEditor();
    }

    private void PopulateEditor()
    {
        if (_editing is null) return;
        _loading = true;
        NameBox.Text = _editing.Name;
        RebuildIconCombo(_editing.IconKey);
        DisplaysPanel.Children.Clear();

        foreach (DisplayDevice display in _displays)
        {
            var checkBox = new CheckBox
            {
                Content = display.Name + (display.IsActive ? string.Empty :
                    (Strings.Ru ? " (неактивен)" : " (inactive)")),
                Tag = display.Id,
                IsChecked = _editing.DisplayIds.Contains(display.Id, StringComparer.OrdinalIgnoreCase)
            };
            checkBox.Checked += DisplaySelectionChanged;
            checkBox.Unchecked += DisplaySelectionChanged;
            DisplaysPanel.Children.Add(checkBox);
        }

        RebuildPrimaryCombo();
        AudioCombo.ItemsSource = _audioDevices;
        AudioCombo.SelectedItem = _audioDevices.FirstOrDefault(d =>
            d.Id.Equals(_editing.AudioDeviceId, StringComparison.OrdinalIgnoreCase));
        if (AudioCombo.SelectedItem is null && _audioDevices.Count > 0)
            AudioCombo.SelectedItem = _audioDevices.FirstOrDefault(d => d.IsDefault) ?? _audioDevices[0];

        DeleteButton.Visibility = _isNew ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Visibility = _isNew ? Visibility.Visible : Visibility.Collapsed;
        EditorTitle.Text = _isNew
            ? (Strings.Ru ? "Новый сценарий" : "New scenario")
            : (Strings.Ru ? "Редактор сценария" : "Scenario editor");
        _loading = false;
    }

    private void DisplaySelectionChanged(object sender, RoutedEventArgs e)
    {
        if (!_loading) RebuildPrimaryCombo();
    }

    private void RebuildPrimaryCombo()
    {
        string? selectedId = (PrimaryCombo.SelectedItem as DisplayDevice)?.Id ??
                             _editing?.PrimaryDisplayId;
        var selected = _displays.Where(d => DisplaysPanel.Children.OfType<CheckBox>()
            .Any(c => c.IsChecked == true && Equals(c.Tag, d.Id))).ToList();
        PrimaryCombo.ItemsSource = selected;
        PrimaryCombo.SelectedItem = selected.FirstOrDefault(d =>
            d.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ??
            selected.FirstOrDefault();
    }

    private bool ReadAndValidate(out Scenario scenario)
    {
        scenario = _editing?.Clone() ?? new Scenario();
        scenario.Name = NameBox.Text.Trim();
        scenario.DisplayIds = DisplaysPanel.Children.OfType<CheckBox>()
            .Where(c => c.IsChecked == true)
            .Select(c => (string)c.Tag)
            .ToList();
        scenario.PrimaryDisplayId = (PrimaryCombo.SelectedItem as DisplayDevice)?.Id ?? string.Empty;
        scenario.AudioDeviceId = (AudioCombo.SelectedItem as AudioDevice)?.Id;
        scenario.IconKey = (IconCombo.SelectedItem as ComboBoxItem)?.Tag as string ?? "monitor";

        if (string.IsNullOrWhiteSpace(scenario.Name))
        {
            ShowInfo(Strings.Ru ? "Введите название сценария." : "Enter a scenario name.",
                InfoBarSeverity.Error);
            NameBox.Focus(FocusState.Programmatic);
            return false;
        }
        if (scenario.DisplayIds.Count == 0)
        {
            ShowInfo(Strings.Ru ? "Выберите хотя бы один экран." : "Select at least one display.",
                InfoBarSeverity.Error);
            return false;
        }
        return true;
    }

    private Scenario SaveEditor()
    {
        if (!ReadAndValidate(out Scenario value))
            throw new InvalidOperationException("Validation failed.");

        int index = _store.Current.Scenarios.FindIndex(s => s.Id == value.Id);
        if (index >= 0)
            _store.Current.Scenarios[index] = value;
        else
            _store.Current.Scenarios.Add(value);
        _store.Save();
        _editing = value.Clone();
        _isNew = false;
        ReloadScenarioList();
        _tray.Refresh();
        ShowInfo(Strings.Ru ? "Сценарий сохранён." : "Scenario saved.", InfoBarSeverity.Success);
        return value;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Scenario value = SaveEditor();
            if (value.DisplayIds.Count > 1)
                await OfferDisplaySettingsAsync();
        }
        catch (InvalidOperationException) { }
    }

    private async void SaveApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Scenario value = SaveEditor();
            await _tray.ApplyScenarioAsync(value);
            if (value.DisplayIds.Count > 1)
                await OfferDisplaySettingsAsync();
        }
        catch (InvalidOperationException) { }
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e) => BeginCreate();

    private async void DevicesButton_Click(object sender, RoutedEventArgs e)
    {
        LoadDevices();
        var panel = new StackPanel { Spacing = 12 };
        var aliasBoxes = new Dictionary<string, (TextBox Box, bool Audio)>();

        panel.Children.Add(new TextBlock
        {
            Text = Strings.Ru ? "Дисплеи" : "Displays",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        foreach (DisplayDevice display in App.Displays.GetDisplays())
        {
            panel.Children.Add(new TextBlock { Text = display.Name, Opacity = 0.68 });
            var box = new TextBox
            {
                Text = _store.Current.DisplayAliases.GetValueOrDefault(display.Id, string.Empty),
                PlaceholderText = display.Name
            };
            aliasBoxes[display.Id] = (box, false);
            panel.Children.Add(box);
        }

        panel.Children.Add(new TextBlock
        {
            Text = Strings.Ru ? "Аудиоустройства" : "Audio devices",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 0)
        });
        foreach (AudioDevice audio in App.Audio.GetRenderDevices())
        {
            panel.Children.Add(new TextBlock { Text = audio.Name, Opacity = 0.68 });
            var box = new TextBox
            {
                Text = _store.Current.AudioAliases.GetValueOrDefault(audio.Id, string.Empty),
                PlaceholderText = audio.Name
            };
            aliasBoxes[audio.Id] = (box, true);
            panel.Children.Add(box);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.Ru ? "Названия устройств" : "Device names",
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 440,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = Strings.Ru ? "Сохранить" : "Save",
            CloseButtonText = Strings.Ru ? "Отмена" : "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        foreach (KeyValuePair<string, (TextBox Box, bool Audio)> pair in aliasBoxes)
        {
            Dictionary<string, string> aliases = pair.Value.Audio
                ? _store.Current.AudioAliases : _store.Current.DisplayAliases;
            string value = pair.Value.Box.Text.Trim();
            if (value.Length == 0)
                aliases.Remove(pair.Key);
            else
                aliases[pair.Key] = value;
        }
        _store.Save();
        LoadDevices();
        PopulateEditor();
        _tray.Refresh();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_store.Current.Scenarios.Count > 0)
            ScenarioList.SelectedIndex = 0;
        else
            BeginCreate();
    }

    private void ScenarioList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading && ScenarioList.SelectedItem is Scenario scenario)
            EditScenario(scenario);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editing is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.Ru ? $"Удалить сценарий «{_editing.Name}»?" : $"Delete “{_editing.Name}”?",
            Content = Strings.Ru
                ? "Его настройки экранов и звука будут удалены."
                : "Its display and audio settings will be deleted.",
            PrimaryButtonText = Strings.Ru ? "Удалить" : "Delete",
            CloseButtonText = Strings.Ru ? "Отмена" : "Cancel",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        bool wasActive = _store.Current.ActiveScenarioId == _editing.Id;
        _store.Current.Scenarios.RemoveAll(s => s.Id == _editing.Id);
        if (wasActive) _store.Current.ActiveScenarioId = null;
        _store.Save();
        _tray.Refresh();
        ReloadScenarioList();

        if (_store.Current.Scenarios.Count > 0)
        {
            ScenarioList.SelectedIndex = 0;
            if (wasActive)
                ShowInfo(Strings.Ru
                    ? "Активный сценарий удалён. Выберите и примените другой сценарий."
                    : "The active scenario was deleted. Select and apply another scenario.",
                    InfoBarSeverity.Warning);
        }
        else
            BeginCreate();
    }

    private async Task OfferDisplaySettingsAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = Strings.Ru ? "Сценарий сохранён" : "Scenario saved",
            Content = Strings.Ru
                ? "Настройте расположение, разрешение и масштаб экранов в параметрах Windows."
                : "Configure display arrangement, resolution, and scale in Windows Settings.",
            PrimaryButtonText = Strings.Ru ? "Открыть параметры экрана" : "Open display settings",
            CloseButtonText = Strings.Ru ? "Позже" : "Later"
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Launcher.LaunchUriAsync(new Uri("ms-settings:display"));
    }

    private void ThemeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ThemeCombo.SelectedIndex < 0) return;
        _store.Current.Theme = (AppTheme)ThemeCombo.SelectedIndex;
        _store.Save();
        ApplyTheme();
        _tray.Refresh();
    }

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = _store.Current.Theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        NativeTheme.Apply(_store.Current.Theme, WindowNative.GetWindowHandle(this));
    }

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageCombo.SelectedIndex < 0) return;
        _store.Current.Language = LanguageCombo.SelectedIndex == 0
            ? AppLanguage.Russian : AppLanguage.English;
        _store.Save();
        ApplyLanguage();
        PopulateEditor();
        _tray.Refresh();
    }

    private void ApplyLanguage()
    {
        bool ru = Strings.Ru;
        CreateButton.Content = ru ? "+ Создать сценарий" : "+ Create scenario";
        DevicesButton.Content = ru ? "Устройства" : "Devices";
        AutostartToggle.Header = ru ? "Автозапуск" : "Start with Windows";
        NameLabel.Text = ru ? "Название" : "Name";
        IconLabel.Text = ru ? "Значок сценария" : "Scenario icon";
        DisplaysLabel.Text = ru ? "Экраны" : "Displays";
        PrimaryLabel.Text = ru ? "Основной экран" : "Primary display";
        AudioLabel.Text = ru ? "Звук" : "Audio";
        SaveButton.Content = ru ? "Сохранить" : "Save";
        SaveApplyButton.Content = ru ? "Сохранить и применить" : "Save and apply";
        CancelButton.Content = ru ? "Отмена" : "Cancel";
        DeleteButton.Content = ru ? "Удалить сценарий" : "Delete scenario";
        AuthorText.Text = ru ? "Автор: sol669 ·" : "Author: sol669 ·";
        SystemThemeItem.Content = ru ? "Системная" : "System";
        LightThemeItem.Content = ru ? "Светлая" : "Light";
        DarkThemeItem.Content = ru ? "Тёмная" : "Dark";
        RebuildIconCombo(_editing?.IconKey ?? "monitor");
    }

    private void RebuildIconCombo(string selectedKey)
    {
        (string Key, string Ru, string En)[] options =
        [
            ("monitor", "Монитор", "Monitor"),
            ("television", "Телевизор", "Television"),
            ("laptop", "Ноутбук", "Laptop"),
            ("dual-monitors", "Два монитора", "Dual monitors"),
            ("projector", "Проектор", "Projector"),
            ("workstation", "Рабочее место", "Workstation"),
            ("gamepad", "Геймпад", "Gamepad"),
            ("headphones", "Наушники", "Headphones"),
            ("speakers", "Колонки", "Speakers"),
            ("sofa", "Диван", "Sofa"),
            ("scenario-1", "Сценарий 1", "Scenario 1"),
            ("scenario-2", "Сценарий 2", "Scenario 2"),
            ("pc-only", "Только экран компьютера", "PC screen only"),
            ("duplicate", "Повторяющийся", "Duplicate"),
            ("extend", "Расширить", "Extend"),
            ("second-only", "Только второй экран", "Second screen only")
        ];
        IconCombo.Items.Clear();
        foreach (var option in options)
            IconCombo.Items.Add(new ComboBoxItem
            {
                Tag = option.Key,
                Content = Strings.Ru ? option.Ru : option.En
            });
        IconCombo.SelectedItem = IconCombo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => Equals(item.Tag, selectedKey)) ?? IconCombo.Items[0];
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        InfoMessage.Message = message;
        InfoMessage.Severity = severity;
        InfoMessage.IsOpen = true;
    }

    internal void RefreshAfterExternalChange()
    {
        ReloadScenarioList();
    }
}
