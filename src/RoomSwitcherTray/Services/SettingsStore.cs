using Microsoft.Win32;
using RoomSwitcherTray.Models;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomSwitcherTray.Services;

public sealed class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "sol669", "Room Switcher Tray");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Room Switcher Tray";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AppSettings Current { get; private set; } = new();

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Current = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                : new();
            Normalize();
            Save();
        }
        catch (Exception ex)
        {
            Log(ex);
            Current = new();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        ApplyAutostart();
    }

    private void Normalize()
    {
        Current.Scenarios ??= [];
        Current.DisplayAliases = new Dictionary<string, string>(
            Current.DisplayAliases ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        Current.AudioAliases = new Dictionary<string, string>(
            Current.AudioAliases ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        foreach (Scenario scenario in Current.Scenarios)
        {
            scenario.Name ??= string.Empty;
            scenario.DisplayIds ??= [];
            scenario.IconKey = string.IsNullOrWhiteSpace(scenario.IconKey)
                ? "monitor" : scenario.IconKey;
        }

        if (Current.ActiveScenarioId is Guid active &&
            Current.Scenarios.All(s => s.Id != active))
            Current.ActiveScenarioId = null;
    }

    public string DisplayName(DisplayDevice device) =>
        Current.DisplayAliases.TryGetValue(device.Id, out string? alias) &&
        !string.IsNullOrWhiteSpace(alias) ? alias.Trim() : device.Name;

    public string AudioName(AudioDevice device) =>
        Current.AudioAliases.TryGetValue(device.Id, out string? alias) &&
        !string.IsNullOrWhiteSpace(alias) ? alias.Trim() : device.Name;

    private void ApplyAutostart()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (Current.StartWithWindows)
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue(RunValueName, false);
        }
        catch (Exception ex)
        {
            Log(ex);
        }
    }

    public static void Log(Exception ex)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "error.log"),
                $"[{DateTime.Now:O}] {ex}\r\n\r\n");
        }
        catch
        {
            Debug.WriteLine(ex);
        }
    }
}

