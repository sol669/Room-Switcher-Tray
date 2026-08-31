namespace RoomSwitcherTray.Core.Services;

internal static class DeviceIdentity
{
    // Shared/system containers cannot identify a particular physical device.
    private static readonly Guid SystemContainer = new("00000000-0000-0000-ffff-ffffffffffff");
    public static bool IsDeviceContainer(Guid? id) => id.HasValue && id.Value != Guid.Empty && id.Value != SystemContainer;
}
