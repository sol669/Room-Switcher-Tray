namespace RoomSwitcherTray.Models;

public sealed class Scenario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<string> DisplayIds { get; set; } = [];
    public string PrimaryDisplayId { get; set; } = string.Empty;
    public string? AudioDeviceId { get; set; }

    public Scenario Clone() => new()
    {
        Id = Id,
        Name = Name,
        DisplayIds = [.. DisplayIds],
        PrimaryDisplayId = PrimaryDisplayId,
        AudioDeviceId = AudioDeviceId
    };

    public override string ToString() => Name;
}
