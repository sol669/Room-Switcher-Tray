using System.Text.Json.Serialization;

namespace RoomSwitcherTray.Core;

public sealed class ScenarioDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<string> DisplayIds { get; set; } = [];
    public string AudioDeviceId { get; set; } = string.Empty;
    public string AudioDeviceContainerId { get; set; } = string.Empty;

    [JsonIgnore]
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(Name) &&
        DisplayIds.Count is >= 1 and <= 4 &&
        DisplayIds.All(id => !string.IsNullOrWhiteSpace(id)) &&
        DisplayIds.Distinct(StringComparer.OrdinalIgnoreCase).Count() == DisplayIds.Count &&
        !string.IsNullOrWhiteSpace(AudioDeviceId);

    public ScenarioDefinition Clone() => new()
    {
        Id = Id,
        Name = Name,
        DisplayIds = [.. DisplayIds],
        AudioDeviceId = AudioDeviceId,
        AudioDeviceContainerId = AudioDeviceContainerId
    };
}

public sealed class AppSettings
{
    public List<ScenarioDefinition> Scenarios { get; set; } = [];
    public Guid? ActiveScenarioId { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyScenarioDefinition? Scenario1 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LegacyScenarioDefinition? Scenario2 { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int ActiveScenario { get; set; }
}

public sealed class LegacyScenarioDefinition
{
    public string Name { get; set; } = string.Empty;
    public string DisplayId { get; set; } = string.Empty;
    public string SecondaryDisplayId { get; set; } = string.Empty;
    public List<string>? DisplayIds { get; set; }
    public string AudioDeviceId { get; set; } = string.Empty;
    public string AudioDeviceContainerId { get; set; } = string.Empty;

    public ScenarioDefinition Upgrade()
    {
        IEnumerable<string> ids = DisplayIds is { Count: > 0 }
            ? DisplayIds
            : new[] { DisplayId, SecondaryDisplayId };
        return new ScenarioDefinition
        {
            Name = Name,
            DisplayIds = ids.Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList(),
            AudioDeviceId = AudioDeviceId,
            AudioDeviceContainerId = AudioDeviceContainerId
        };
    }
}

public sealed record DisplayDevice(
    string Id,
    string Name,
    bool IsActive,
    bool IsAvailable,
    Guid? ContainerId)
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

public enum AudioDeviceKind
{
    Other,
    Display
}

public sealed record AudioDevice(
    string Id,
    string Name,
    bool IsDefault,
    AudioDeviceState State,
    Guid? ContainerId,
    AudioDeviceKind Kind,
    string? DisplayName = null)
{
    public bool IsActive => State == AudioDeviceState.Active;

    public override string ToString()
    {
        string label = Kind == AudioDeviceKind.Display && !string.IsNullOrWhiteSpace(DisplayName)
            ? $"{DisplayName} — HDMI/DisplayPort"
            : Name;
        return IsDefault ? $"{label} — используется" :
            State == AudioDeviceState.Disabled ? $"{label} — отключено в Windows" :
            State != AudioDeviceState.Active ? $"{label} — сейчас недоступно" :
            label;
    }
}

public sealed record ApplyResult(bool Success, string Message);
