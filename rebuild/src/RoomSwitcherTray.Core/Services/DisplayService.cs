using RoomSwitcherTray.Core.Interop;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core.Services;

public sealed class DisplayService
{
    public IReadOnlyList<DisplayDevice> GetDisplays()
    {
        DisplayNative.PATH_INFO[] paths = QueryPaths();
        var result = new Dictionary<string, DisplayDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (DisplayNative.PATH_INFO path in paths)
        {
            (string id, string name) = GetIdentity(path.targetInfo.adapterId, path.targetInfo.id);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            bool active = (path.flags & DisplayNative.DISPLAYCONFIG_PATH_ACTIVE) != 0;
            var device = new DisplayDevice(id, name, active, path.targetInfo.targetAvailable || active);
            if (!result.TryGetValue(id, out DisplayDevice? existing) || (!existing.IsActive && active))
                result[id] = device;
        }

        return result.Values
            .Where(device => device.IsAvailable)
            .OrderByDescending(device => device.IsActive)
            .ThenBy(device => device.Name)
            .ToList();
    }

    public void ApplySingleDisplay(string displayId)
    {
        DisplayNative.PATH_INFO[] allPaths = QueryPaths();
        DisplayNative.PATH_INFO? selected = null;

        foreach (DisplayNative.PATH_INFO candidate in allPaths
                     .OrderByDescending(path => (path.flags & DisplayNative.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                     .ThenByDescending(path => path.targetInfo.targetAvailable))
        {
            (string id, _) = GetIdentity(candidate.targetInfo.adapterId, candidate.targetInfo.id);
            if (id.Equals(displayId, StringComparison.OrdinalIgnoreCase))
            {
                selected = candidate;
                break;
            }
        }

        if (selected is null)
            throw new InvalidOperationException("Выбранный дисплей сейчас недоступен.");

        DisplayNative.PATH_INFO path = selected.Value;
        path.flags = DisplayNative.DISPLAYCONFIG_PATH_ACTIVE;
        path.sourceInfo.modeInfoIdx = DisplayNative.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        path.targetInfo.modeInfoIdx = DisplayNative.DISPLAYCONFIG_PATH_MODE_IDX_INVALID;
        DisplayNative.PATH_INFO[] requested = [path];

        uint common = DisplayNative.SDC_USE_SUPPLIED_DISPLAY_CONFIG | DisplayNative.SDC_ALLOW_CHANGES;
        int error = DisplayNative.SetDisplayConfig(1, requested, 0, null,
            common | DisplayNative.SDC_VALIDATE);
        if (error != 0)
            throw new Win32Exception(error, "Windows отклонила выбранную конфигурацию дисплея.");

        error = DisplayNative.SetDisplayConfig(1, requested, 0, null,
            common | DisplayNative.SDC_APPLY | DisplayNative.SDC_SAVE_TO_DATABASE);
        if (error != 0)
            throw new Win32Exception(error, "Не удалось включить выбранный дисплей.");
    }

    private static DisplayNative.PATH_INFO[] QueryPaths()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int error = DisplayNative.GetDisplayConfigBufferSizes(DisplayNative.QDC_ALL_PATHS,
                out uint pathCount, out uint modeCount);
            if (error != 0) throw new Win32Exception(error);

            var paths = new DisplayNative.PATH_INFO[pathCount];
            var modes = new DisplayNative.MODE_INFO[modeCount];
            error = DisplayNative.QueryDisplayConfig(DisplayNative.QDC_ALL_PATHS,
                ref pathCount, paths, ref modeCount, modes, nint.Zero);
            if (error == 0)
            {
                Array.Resize(ref paths, (int)pathCount);
                return paths;
            }
            if (error != 122) throw new Win32Exception(error);
        }
        throw new Win32Exception(122);
    }

    private static (string Id, string Name) GetIdentity(DisplayNative.LUID adapterId, uint targetId)
    {
        var request = new DisplayNative.TARGET_DEVICE_NAME
        {
            header = new DisplayNative.DEVICE_INFO_HEADER
            {
                type = 2,
                size = (uint)Marshal.SizeOf<DisplayNative.TARGET_DEVICE_NAME>(),
                adapterId = adapterId,
                id = targetId
            }
        };
        if (DisplayNative.DisplayConfigGetDeviceInfo(ref request) != 0)
            return (string.Empty, string.Empty);

        string name = string.IsNullOrWhiteSpace(request.monitorFriendlyDeviceName)
            ? $"Дисплей {targetId + 1}"
            : request.monitorFriendlyDeviceName;
        return (request.monitorDevicePath ?? string.Empty, name);
    }
}
