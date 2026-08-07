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
    public bool IsConfigured =>
        Current.Scenario1?.IsComplete == true && Current.Scenario2?.IsComplete == true;

    public void Load()
    {
        try
        {
            Current = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new()
                : new();
            if (!IsConfigured)
                Current.ActiveScenario = 0;
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
