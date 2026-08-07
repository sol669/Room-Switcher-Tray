using RoomSwitcherTray.Core.Services;
using Forms = System.Windows.Forms;

namespace RoomSwitcherTray.Core;

/// <summary>
/// Небольшое системное окно для проверки ядра переключения сценариев.
/// Оно намеренно не использует XAML: ошибка интерфейса не должна ронять трей.
/// </summary>
public sealed class SettingsWindow : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly TrayService _tray;
    private readonly Forms.Form _form;
    private readonly Forms.TextBox _scenario1Name = new();
    private readonly Forms.TextBox _scenario2Name = new();
    private readonly Forms.ComboBox _scenario1Display = new();
    private readonly Forms.ComboBox _scenario2Display = new();
    private readonly Forms.ComboBox _scenario1Audio = new();
    private readonly Forms.ComboBox _scenario2Audio = new();
    private readonly Forms.Label _status = new();
    private readonly Forms.Button _refresh = new();
    private readonly Forms.Button _save = new();
    private IReadOnlyList<DisplayDevice> _displays = [];
    private IReadOnlyList<AudioDevice> _audioDevices = [];
    private bool _loaded;

    public event EventHandler? Closed;

    public SettingsWindow(SettingsStore settings, TrayService tray)
    {
        _settings = settings;
        _tray = tray;
        _form = BuildForm();
        _form.Shown += async (_, _) => await LoadDevicesOnceAsync();
        _form.FormClosed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
    }

    public void Activate()
    {
        if (!_form.Visible)
            _form.Show();
        else
        {
            _form.WindowState = Forms.FormWindowState.Normal;
            _form.Activate();
            _form.BringToFront();
        }
    }

    private Forms.Form BuildForm()
    {
        var form = new Forms.Form
        {
            Text = "Room Switcher Tray — сценарии",
            StartPosition = Forms.FormStartPosition.CenterScreen,
            ClientSize = new System.Drawing.Size(720, 650),
            MinimumSize = new System.Drawing.Size(640, 600),
            AutoScaleMode = Forms.AutoScaleMode.Dpi,
            Font = new System.Drawing.Font("Segoe UI", 10F),
            MaximizeBox = false
        };

        var root = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            Padding = new Forms.Padding(24),
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
        root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 50));
        root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 50));
        root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
        root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));
        root.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.AutoSize));

        var title = new Forms.Label
        {
            AutoSize = true,
            Text = "Настройте два сценария переключения",
            Font = new System.Drawing.Font("Segoe UI Semibold", 16F),
            Margin = new Forms.Padding(0, 0, 0, 16)
        };
        root.Controls.Add(title, 0, 0);
        root.Controls.Add(BuildScenarioGroup(1), 0, 1);
        root.Controls.Add(BuildScenarioGroup(2), 0, 2);

        _status.AutoSize = true;
        _status.ForeColor = System.Drawing.Color.Firebrick;
        _status.Margin = new Forms.Padding(0, 8, 0, 8);
        root.Controls.Add(_status, 0, 3);

        _refresh.Text = "Обновить список устройств";
        _refresh.AutoSize = true;
        _refresh.Click += async (_, _) => await ReloadDevicesAsync();
        root.Controls.Add(_refresh, 0, 4);

        var actions = new Forms.FlowLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            AutoSize = true,
            FlowDirection = Forms.FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Forms.Padding(0, 18, 0, 0)
        };
        _save.Text = "Сохранить";
        _save.AutoSize = true;
        _save.Padding = new Forms.Padding(14, 5, 14, 5);
        _save.Click += Save_Click;
        var cancel = new Forms.Button
        {
            Text = "Отмена",
            AutoSize = true,
            Padding = new Forms.Padding(14, 5, 14, 5)
        };
        cancel.Click += (_, _) => _form.Close();
        actions.Controls.Add(_save);
        actions.Controls.Add(cancel);
        root.Controls.Add(actions, 0, 5);
        form.Controls.Add(root);
        form.AcceptButton = _save;
        form.CancelButton = cancel;
        return form;
    }

    private Forms.GroupBox BuildScenarioGroup(int slot)
    {
        bool first = slot == 1;
        Forms.TextBox name = first ? _scenario1Name : _scenario2Name;
        Forms.ComboBox display = first ? _scenario1Display : _scenario2Display;
        Forms.ComboBox audio = first ? _scenario1Audio : _scenario2Audio;
        display.DropDownStyle = Forms.ComboBoxStyle.DropDownList;
        audio.DropDownStyle = Forms.ComboBoxStyle.DropDownList;

        var grid = new Forms.TableLayoutPanel
        {
            Dock = Forms.DockStyle.Fill,
            Padding = new Forms.Padding(12),
            ColumnCount = 2,
            RowCount = 3
        };
        grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Absolute, 160));
        grid.ColumnStyles.Add(new Forms.ColumnStyle(Forms.SizeType.Percent, 100));
        for (int row = 0; row < 3; row++)
            grid.RowStyles.Add(new Forms.RowStyle(Forms.SizeType.Percent, 33.33F));
        AddRow(grid, 0, "Название", name);
        AddRow(grid, 1, "Дисплей", display);
        AddRow(grid, 2, "Аудиоустройство", audio);
        return new Forms.GroupBox
        {
            Text = $"Сценарий {slot}",
            Dock = Forms.DockStyle.Fill,
            Margin = new Forms.Padding(0, 0, 0, 12),
            Controls = { grid }
        };
    }

    private static void AddRow(Forms.TableLayoutPanel grid, int row, string caption, Forms.Control control)
    {
        var label = new Forms.Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = Forms.AnchorStyles.Left
        };
        control.Dock = Forms.DockStyle.Fill;
        control.Margin = new Forms.Padding(3, 8, 3, 8);
        grid.Controls.Add(label, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private async Task LoadDevicesOnceAsync()
    {
        if (_loaded) return;
        _loaded = true;
        await ReloadDevicesAsync();
    }

    private async Task ReloadDevicesAsync()
    {
        SetBusy(true);
        _status.Text = "Поиск устройств…";
        try
        {
            ScenarioDefinition first = ReadScenario(1);
            ScenarioDefinition second = ReadScenario(2);
            _displays = await Task.Run(App.Displays.GetDisplays);
            _audioDevices = await App.Audio.GetRenderDevicesAsync();
            Bind(_scenario1Display, _scenario1Audio,
                first.DisplayId.Length > 0 ? first.DisplayId : _settings.Current.Scenario1?.DisplayId,
                first.AudioDeviceId.Length > 0 ? first.AudioDeviceId : _settings.Current.Scenario1?.AudioDeviceId);
            Bind(_scenario2Display, _scenario2Audio,
                second.DisplayId.Length > 0 ? second.DisplayId : _settings.Current.Scenario2?.DisplayId,
                second.AudioDeviceId.Length > 0 ? second.AudioDeviceId : _settings.Current.Scenario2?.AudioDeviceId);
            _scenario1Name.Text = first.Name.Length > 0 ? first.Name : _settings.Current.Scenario1?.Name ?? string.Empty;
            _scenario2Name.Text = second.Name.Length > 0 ? second.Name : _settings.Current.Scenario2?.Name ?? string.Empty;
            _status.Text = _displays.Count == 0 || _audioDevices.Count == 0
                ? "Не удалось найти все необходимые устройства."
                : string.Empty;
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            _status.Text = $"Не удалось получить список устройств: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Bind(Forms.ComboBox display, Forms.ComboBox audio, string? displayId, string? audioId)
    {
        display.DataSource = _displays.ToList();
        audio.DataSource = _audioDevices.ToList();
        display.SelectedItem = _displays.FirstOrDefault(x => x.Id.Equals(displayId, StringComparison.OrdinalIgnoreCase));
        audio.SelectedItem = _audioDevices.FirstOrDefault(x => x.Id.Equals(audioId, StringComparison.OrdinalIgnoreCase));
    }

    private ScenarioDefinition ReadScenario(int slot)
    {
        bool first = slot == 1;
        return new ScenarioDefinition
        {
            Name = (first ? _scenario1Name : _scenario2Name).Text.Trim(),
            DisplayId = ((first ? _scenario1Display : _scenario2Display).SelectedItem as DisplayDevice)?.Id ?? string.Empty,
            AudioDeviceId = ((first ? _scenario1Audio : _scenario2Audio).SelectedItem as AudioDevice)?.Id ?? string.Empty
        };
    }

    private void Save_Click(object? sender, EventArgs e)
    {
        ScenarioDefinition first = ReadScenario(1);
        ScenarioDefinition second = ReadScenario(2);
        if (!first.IsComplete || !second.IsComplete)
        {
            _status.Text = "Для обоих сценариев укажите название, дисплей и аудиоустройство.";
            return;
        }

        try
        {
            _settings.Current.Scenario1 = first;
            _settings.Current.Scenario2 = second;
            _settings.Save();
            _tray.Refresh();
            _form.Close();
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            _status.Text = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }

    private void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        _save.Enabled = !busy;
    }

    public void Dispose() => _form.Dispose();
}
