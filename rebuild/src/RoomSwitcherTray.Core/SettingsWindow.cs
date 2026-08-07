using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoomSwitcherTray.Core.Services;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace RoomSwitcherTray.Core;

public sealed class SettingsWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly TrayService _tray;
    private readonly TextBox _scenario1Name = new();
    private readonly TextBox _scenario2Name = new();
    private readonly ComboBox _scenario1Display = new();
    private readonly ComboBox _scenario2Display = new();
    private readonly ComboBox _scenario1Audio = new();
    private readonly ComboBox _scenario2Audio = new();
    private readonly InfoBar _statusBar = new() { IsOpen = false, IsClosable = true };
    private readonly Button _refreshButton = new() { Content = "Обновить список устройств" };
    private readonly Button _saveButton = new() { Content = "Сохранить", MinWidth = 120 };
    private IReadOnlyList<DisplayDevice> _displays = [];
    private IReadOnlyList<AudioDevice> _audioDevices = [];
    private bool _loaded;

    public SettingsWindow(SettingsStore settings, TrayService tray)
    {
        _settings = settings;
        _tray = tray;
        Title = "Room Switcher Tray";
        Content = BuildContent();
        Activated += SettingsWindow_Activated;
    }

    private FrameworkElement BuildContent()
    {
        var root = new Grid { Background = GetBrush("ApplicationPageBackgroundThemeBrush") };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var content = new StackPanel
        {
            Width = 760,
            MaxWidth = 760,
            Margin = new Thickness(40, 32, 40, 28),
            Spacing = 20
        };
        content.Children.Add(new TextBlock
        {
            Text = "Room Switcher Tray",
            FontSize = 28,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        content.Children.Add(new TextBlock
        {
            Text = "Настройте два сценария переключения",
            FontSize = 15,
            Opacity = 0.68,
            Margin = new Thickness(0, -14, 0, 0)
        });
        content.Children.Add(_statusBar);
        content.Children.Add(BuildScenarioCard(1));
        content.Children.Add(BuildScenarioCard(2));

        _refreshButton.HorizontalAlignment = HorizontalAlignment.Left;
        _refreshButton.Click += async (_, _) => await ReloadDevicesAsync();
        content.Children.Add(_refreshButton);

        var scroll = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(scroll, 0);
        root.Children.Add(scroll);

        var closeButton = new Button { Content = "Закрыть", MinWidth = 120 };
        closeButton.Click += (_, _) => Close();
        _saveButton.Click += SaveButton_Click;
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out object accent) && accent is Style style)
            _saveButton.Style = style;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12
        };
        actions.Children.Add(closeButton);
        actions.Children.Add(_saveButton);
        var footer = new Border
        {
            Padding = new Thickness(24, 16, 24, 16),
            BorderThickness = new Thickness(0, 1, 0, 0),
            BorderBrush = GetBrush("DividerStrokeColorDefaultBrush"),
            Background = GetBrush("ApplicationPageBackgroundThemeBrush"),
            Child = actions
        };
        Grid.SetRow(footer, 1);
        root.Children.Add(footer);
        return root;
    }

    private Border BuildScenarioCard(int slot)
    {
        bool first = slot == 1;
        TextBox name = first ? _scenario1Name : _scenario2Name;
        ComboBox display = first ? _scenario1Display : _scenario2Display;
        ComboBox audio = first ? _scenario1Audio : _scenario2Audio;
        name.Header = "Название";
        name.PlaceholderText = first ? "Например, Компьютер" : "Например, Телевизор";
        display.Header = "Дисплей";
        display.PlaceholderText = "Выберите дисплей";
        display.HorizontalAlignment = HorizontalAlignment.Stretch;
        audio.Header = "Аудиоустройство";
        audio.PlaceholderText = "Выберите аудиоустройство";
        audio.HorizontalAlignment = HorizontalAlignment.Stretch;

        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock
        {
            Text = $"Сценарий {slot}",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(name);
        panel.Children.Add(display);
        panel.Children.Add(audio);
        return new Border
        {
            Padding = new Thickness(24),
            CornerRadius = new CornerRadius(8),
            Background = GetBrush("CardBackgroundFillColorDefaultBrush"),
            BorderBrush = GetBrush("CardStrokeColorDefaultBrush"),
            BorderThickness = new Thickness(1),
            Child = panel
        };
    }

    private static Brush GetBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out object value) && value is Brush brush)
            return brush;
        return new SolidColorBrush(Colors.Transparent);
    }

    private async void SettingsWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (_loaded) return;
        _loaded = true;
        CenterAndSize();
        NativeTheme.Apply(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
        await ReloadDevicesAsync();
    }

    private void CenterAndSize()
    {
        nint window = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        uint dpi = GetDpiForWindow(window);
        double scale = Math.Max(1, dpi / 96d);
        DisplayArea area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        RectInt32 work = area.WorkArea;
        int width = Math.Min((int)Math.Round(860 * scale), Math.Max(640, work.Width - 48));
        int height = Math.Min((int)Math.Round(820 * scale), Math.Max(600, work.Height - 48));
        AppWindow.Resize(new SizeInt32(width, height));
        AppWindow.Move(new PointInt32(work.X + (work.Width - width) / 2,
            work.Y + (work.Height - height) / 2));
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
            presenter.IsResizable = true;
        }
    }

    private async Task ReloadDevicesAsync()
    {
        SetBusy(true);
        try
        {
            ScenarioDefinition first = ReadScenarioSelection(1);
            ScenarioDefinition second = ReadScenarioSelection(2);
            _displays = await Task.Run(App.Displays.GetDisplays);
            _audioDevices = await App.Audio.GetRenderDevicesAsync();
            BindDevices(_scenario1Display, _scenario1Audio,
                first.DisplayId.Length > 0 ? first.DisplayId : _settings.Current.Scenario1?.DisplayId,
                first.AudioDeviceId.Length > 0 ? first.AudioDeviceId : _settings.Current.Scenario1?.AudioDeviceId);
            BindDevices(_scenario2Display, _scenario2Audio,
                second.DisplayId.Length > 0 ? second.DisplayId : _settings.Current.Scenario2?.DisplayId,
                second.AudioDeviceId.Length > 0 ? second.AudioDeviceId : _settings.Current.Scenario2?.AudioDeviceId);
            _scenario1Name.Text = first.Name.Length > 0 ? first.Name : _settings.Current.Scenario1?.Name ?? string.Empty;
            _scenario2Name.Text = second.Name.Length > 0 ? second.Name : _settings.Current.Scenario2?.Name ?? string.Empty;
            if (_displays.Count == 0 || _audioDevices.Count == 0)
                ShowStatus("Не удалось найти все необходимые устройства.", InfoBarSeverity.Warning);
            else
                _statusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            ShowStatus($"Не удалось получить список устройств: {ex.Message}", InfoBarSeverity.Error);
        }
        finally { SetBusy(false); }
    }

    private void BindDevices(ComboBox displayBox, ComboBox audioBox,
        string? selectedDisplayId, string? selectedAudioId)
    {
        displayBox.ItemsSource = _displays;
        audioBox.ItemsSource = _audioDevices;
        displayBox.SelectedItem = _displays.FirstOrDefault(device =>
            device.Id.Equals(selectedDisplayId, StringComparison.OrdinalIgnoreCase));
        audioBox.SelectedItem = _audioDevices.FirstOrDefault(device =>
            device.Id.Equals(selectedAudioId, StringComparison.OrdinalIgnoreCase));
    }

    private ScenarioDefinition ReadScenarioSelection(int slot)
    {
        bool first = slot == 1;
        return new ScenarioDefinition
        {
            Name = (first ? _scenario1Name : _scenario2Name).Text.Trim(),
            DisplayId = ((first ? _scenario1Display : _scenario2Display).SelectedItem as DisplayDevice)?.Id ?? string.Empty,
            AudioDeviceId = ((first ? _scenario1Audio : _scenario2Audio).SelectedItem as AudioDevice)?.Id ?? string.Empty
        };
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ScenarioDefinition first = ReadScenarioSelection(1);
        ScenarioDefinition second = ReadScenarioSelection(2);
        if (!first.IsComplete || !second.IsComplete)
        {
            ShowStatus("Для обоих сценариев укажите название, дисплей и аудиоустройство.", InfoBarSeverity.Warning);
            return;
        }
        _settings.Current.Scenario1 = first;
        _settings.Current.Scenario2 = second;
        if (_settings.Current.ActiveScenario is not 1 and not 2)
            _settings.Current.ActiveScenario = 0;
        try
        {
            _settings.Save();
            _tray.Refresh();
            Close();
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            ShowStatus($"Не удалось сохранить настройки: {ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void SetBusy(bool busy)
    {
        _refreshButton.IsEnabled = !busy;
        _saveButton.IsEnabled = !busy;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        _statusBar.Message = message;
        _statusBar.Severity = severity;
        _statusBar.IsOpen = true;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
