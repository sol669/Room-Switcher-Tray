namespace RoomSwitcherTray.Core;

public sealed class ScenarioDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayId { get; set; } = string.Empty;
    public string SecondaryDisplayId { get; set; } = string.Empty;
    public string AudioDeviceId { get; set; } = string.Empty;

    public IReadOnlyList<string> DisplayIds => string.IsNullOrWhiteSpace(SecondaryDisplayId) ||
        SecondaryDisplayId.Equals(DisplayId, StringComparison.OrdinalIgnoreCase)
        ? [DisplayId]
        : [DisplayId, SecondaryDisplayId];

    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name) &&
        !string.IsNullOrWhiteSpace(DisplayId) &&
        !string.IsNullOrWhiteSpace(AudioDeviceId);

    public ScenarioDefinition Clone() => new()
    {
        Name = Name,
        DisplayId = DisplayId,
        SecondaryDisplayId = SecondaryDisplayId,
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

public enum AudioDeviceState
{
    Active,
    Disabled,
    NotPresent,
    Unplugged
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault, AudioDeviceState State)
{
    public bool IsActive => State == AudioDeviceState.Active;

    public override string ToString() =>
        IsDefault ? $"{Name} — используется" :
        State == AudioDeviceState.Disabled ? $"{Name} — отключено в Windows" :
        State != AudioDeviceState.Active ? $"{Name} — сейчас недоступно" :
        Name;
}

public sealed record ApplyResult(bool Success, string Message);
