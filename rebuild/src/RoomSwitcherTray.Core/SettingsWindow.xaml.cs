using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RoomSwitcherTray.Core.Services;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace RoomSwitcherTray.Core;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly TrayService _tray;
    private IReadOnlyList<DisplayDevice> _displays = [];
    private IReadOnlyList<AudioDevice> _audioDevices = [];
    private bool _loaded;

    public SettingsWindow(SettingsStore settings, TrayService tray)
    {
        _settings = settings;
        _tray = tray;
        InitializeComponent();
        Title = "Room Switcher Tray";
        Activated += SettingsWindow_Activated;
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
        AppWindow.Move(new PointInt32(
            work.X + (work.Width - width) / 2,
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
            ScenarioDefinition? first = ReadScenarioSelection(1, allowIncomplete: true);
            ScenarioDefinition? second = ReadScenarioSelection(2, allowIncomplete: true);
            _displays = await Task.Run(App.Displays.GetDisplays);
            _audioDevices = await App.Audio.GetRenderDevicesAsync();

            BindDevices(Scenario1Display, Scenario1Audio,
                first?.DisplayId ?? _settings.Current.Scenario1?.DisplayId,
                first?.AudioDeviceId ?? _settings.Current.Scenario1?.AudioDeviceId);
            BindDevices(Scenario2Display, Scenario2Audio,
                second?.DisplayId ?? _settings.Current.Scenario2?.DisplayId,
                second?.AudioDeviceId ?? _settings.Current.Scenario2?.AudioDeviceId);

            Scenario1Name.Text = first?.Name ?? _settings.Current.Scenario1?.Name ?? string.Empty;
            Scenario2Name.Text = second?.Name ?? _settings.Current.Scenario2?.Name ?? string.Empty;

            if (_displays.Count == 0 || _audioDevices.Count == 0)
                ShowStatus("Не удалось найти все необходимые устройства.", InfoBarSeverity.Warning);
            else
                StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            ShowStatus($"Не удалось получить список устройств: {ex.Message}", InfoBarSeverity.Error);
        }
        finally
        {
            SetBusy(false);
        }
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

    private ScenarioDefinition? ReadScenarioSelection(int slot, bool allowIncomplete)
    {
        TextBox name = slot == 1 ? Scenario1Name : Scenario2Name;
        ComboBox display = slot == 1 ? Scenario1Display : Scenario2Display;
        ComboBox audio = slot == 1 ? Scenario1Audio : Scenario2Audio;
        var scenario = new ScenarioDefinition
        {
            Name = name.Text.Trim(),
            DisplayId = (display.SelectedItem as DisplayDevice)?.Id ?? string.Empty,
            AudioDeviceId = (audio.SelectedItem as AudioDevice)?.Id ?? string.Empty
        };
        return allowIncomplete || scenario.IsComplete ? scenario : null;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) =>
        await ReloadDevicesAsync();

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        ScenarioDefinition? first = ReadScenarioSelection(1, allowIncomplete: false);
        ScenarioDefinition? second = ReadScenarioSelection(2, allowIncomplete: false);
        if (first is null || second is null)
        {
            ShowStatus("Для обоих сценариев укажите название, дисплей и аудиоустройство.",
                InfoBarSeverity.Warning);
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

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        RefreshButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
    }

    private void ShowStatus(string message, InfoBarSeverity severity)
    {
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
}
