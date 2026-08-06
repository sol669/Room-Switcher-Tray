namespace RoomSwitcherTray.Models;

public sealed record DisplayDevice(
    string Id,
    string Name,
    bool IsAvailable,
    bool IsActive,
    bool IsPrimary,
    long AdapterId,
    uint TargetId,
    uint Width,
    uint Height,
    double RefreshRate,
    bool HdrSupported,
    bool HdrEnabled)
{
    public override string ToString() => Name;

    public string Status =>
        $"{Width}×{Height} · {Math.Round(RefreshRate):0} Hz · {(HdrEnabled ? "HDR" : "SDR")}";
}

public sealed record AudioDevice(string Id, string Name, bool IsDefault, int VolumePercent)
{
    public override string ToString() => Name;
}

public sealed record ScenarioApplyResult(bool Success, string Message)
{
    public static ScenarioApplyResult Ok(string message) => new(true, message);
    public static ScenarioApplyResult Error(string message) => new(false, message);
}

