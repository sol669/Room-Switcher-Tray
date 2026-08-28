using System.Diagnostics;
using System.Text.Json;

namespace RoomSwitcherTray.Core.Services;

public sealed class SettingsStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "sol669", "Room Switcher Tray", "CoreRebuild");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();
    public bool IsConfigured => Current.Scenarios.Count > 0 && Current.Scenarios.All(s => s.IsComplete);

    public void Load()
    {
        try
        {
            Current = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                : new();
            UpgradeLegacySettings();
            if (!IsConfigured)
                Current.ActiveScenarioId = null;
        }
        catch (Exception ex)
        {
            Log(ex);
            Current = new();
        }
    }

    private void UpgradeLegacySettings()
    {
        if (Current.Scenarios.Count == 0)
        {
            if (Current.Scenario1 is not null) Current.Scenarios.Add(Current.Scenario1.Upgrade());
            if (Current.Scenario2 is not null) Current.Scenarios.Add(Current.Scenario2.Upgrade());
            if (Current.ActiveScenario is 1 or 2 && Current.Scenarios.Count >= Current.ActiveScenario)
                Current.ActiveScenarioId = Current.Scenarios[Current.ActiveScenario - 1].Id;
        }
        Current.Scenario1 = null;
        Current.Scenario2 = null;
        Current.ActiveScenario = 0;
        if (Current.ActiveScenarioId.HasValue &&
            Current.Scenarios.All(scenario => scenario.Id != Current.ActiveScenarioId.Value))
            Current.ActiveScenarioId = null;
    }

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
    }

    public static void Log(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.AppendAllText(Path.Combine(Folder, "error.log"),
                $"[{DateTime.Now:O}] {exception}\r\n\r\n");
        }
        catch
        {
            Debug.WriteLine(exception);
        }
    }
}
