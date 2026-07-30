namespace RoomSwitcherTray.Models;

public sealed record DisplayDevice(
    string Id,
    string Name,
    bool IsAvailable,
    bool IsActive,
    bool IsPrimary,
    long AdapterId,
    uint TargetId)
{
    public override string ToString() => Name;
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault)
{
    public override string ToString() => Name;
}

public sealed record ScenarioApplyResult(bool Success, string Message)
{
    public static ScenarioApplyResult Ok(string message) => new(true, message);
    public static ScenarioApplyResult Error(string message) => new(false, message);
}
