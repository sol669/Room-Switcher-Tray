namespace RoomSwitcherTray.Core;

public sealed class ScenarioDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayId { get; set; } = string.Empty;
    public string AudioDeviceId { get; set; } = string.Empty;

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(DisplayId) &&
        !string.IsNullOrWhiteSpace(AudioDeviceId);

    public ScenarioDefinition Clone() => new()
    {
        Name = Name,
        DisplayId = DisplayId,
        AudioDeviceId = AudioDeviceId
    };
}

public sealed class AppSettings
{
    public ScenarioDefinition? Scenario1 { get; set; }
    public ScenarioDefinition? Scenario2 { get; set; }
    public int ActiveScenario { get; set; }
}

public sealed record DisplayDevice(string Id, string Name, bool IsActive, bool IsAvailable)
{
    public override string ToString() => IsActive ? $"{Name} — используется" : Name;
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault)
{
    public override string ToString() => IsDefault ? $"{Name} — используется" : Name;
}

public sealed record ApplyResult(bool Success, string Message);
