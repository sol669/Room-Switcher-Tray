using RoomSwitcherTray.Interop;
using RoomSwitcherTray.Models;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Services;

public sealed class DisplayService
{
    public IReadOnlyList<DisplayDevice> GetDisplays()
    {
        Query(out NativeMethods.DISPLAYCONFIG_PATH_INFO[] paths,
            out NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes);
        var result = new Dictionary<string, DisplayDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (NativeMethods.DISPLAYCONFIG_PATH_INFO path in paths)
        {
            string id = GetMonitorPath(path.targetInfo.adapterId, path.targetInfo.id);
            if (string.IsNullOrWhiteSpace(id))
                continue;

            string name = GetFriendlyName(path.targetInfo.adapterId, path.targetInfo.id);
            bool active = (path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0;
            bool primary = active && IsPrimary(path, modes);
            result[id] = new DisplayDevice(id, name, true, active, primary,
                path.targetInfo.adapterId.ToInt64(), path.targetInfo.id);
        }

        return result.Values
            .OrderByDescending(d => d.IsPrimary)
            .ThenByDescending(d => d.IsActive)
            .ThenBy(d => d.Name)
            .ToList();
    }

    public void Apply(IReadOnlyCollection<string> selectedIds, string primaryId)
    {
        Query(out NativeMethods.DISPLAYCONFIG_PATH_INFO[] allPaths,
            out NativeMethods.DISPLAYCONFIG_MODE_INFO[] allModes);

        var candidates = allPaths
            .Select(p => (Id: GetMonitorPath(p.targetInfo.adapterId, p.targetInfo.id), Path: p))
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Path).ToList(),
                StringComparer.OrdinalIgnoreCase);
        string[] missing = selectedIds.Where(id => !candidates.ContainsKey(id)).ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Unavailable displays: {string.Join(", ", missing)}");

        var selected = new HashSet<string>(selectedIds, StringComparer.OrdinalIgnoreCase);
        var paths = new List<NativeMethods.DISPLAYCONFIG_PATH_INFO>();
        foreach (string id in selected)
        {
            NativeMethods.DISPLAYCONFIG_PATH_INFO path = candidates[id]
                .OrderByDescending(candidate =>
                    (candidate.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0)
                .ThenByDescending(candidate => candidate.targetInfo.targetAvailable)
                .First();
            path.flags |= NativeMethods.DISPLAYCONFIG_PATH_ACTIVE;
            paths.Add(path);
        }

        if (paths.Count == 0)
            throw new InvalidOperationException("At least one display must be selected.");

        uint flags = NativeMethods.SDC_APPLY |
                     NativeMethods.SDC_USE_SUPPLIED_DISPLAY_CONFIG |
                     NativeMethods.SDC_ALLOW_CHANGES |
                     NativeMethods.SDC_SAVE_TO_DATABASE |
                     NativeMethods.SDC_VIRTUAL_MODE_AWARE;
        int error = NativeMethods.SetDisplayConfig((uint)paths.Count, paths.ToArray(),
            (uint)allModes.Length, allModes, flags);
        if (error != 0)
            throw new Win32Exception(error);

        SetPrimaryDisplay(primaryId);
    }

    private static void SetPrimaryDisplay(string primaryId)
    {
        Query(out NativeMethods.DISPLAYCONFIG_PATH_INFO[] paths,
            out NativeMethods.DISPLAYCONFIG_MODE_INFO[] _);
        foreach (NativeMethods.DISPLAYCONFIG_PATH_INFO path in paths)
        {
            bool active = (path.flags & NativeMethods.DISPLAYCONFIG_PATH_ACTIVE) != 0;
            string id = GetMonitorPath(path.targetInfo.adapterId, path.targetInfo.id);
            if (!active || !id.Equals(primaryId, StringComparison.OrdinalIgnoreCase))
                continue;

            string gdiName = GetGdiName(path.sourceInfo.adapterId, path.sourceInfo.id);
            if (string.IsNullOrWhiteSpace(gdiName))
                throw new InvalidOperationException("The primary display has no GDI name.");

            var mode = new NativeMethods.DEVMODE
            {
                dmSize = (ushort)Marshal.SizeOf<NativeMethods.DEVMODE>(),
                dmFields = NativeMethods.DM_POSITION,
                dmPositionX = 0,
                dmPositionY = 0
            };
            int result = NativeMethods.ChangeDisplaySettingsEx(gdiName, ref mode, nint.Zero,
                NativeMethods.CDS_SET_PRIMARY | NativeMethods.CDS_UPDATEREGISTRY |
                NativeMethods.CDS_NORESET, nint.Zero);
            if (result != 0)
                throw new Win32Exception(result, "Could not set the primary display.");
            NativeMethods.ChangeDisplaySettingsEx(null, nint.Zero, nint.Zero, 0, nint.Zero);
            return;
        }
        throw new InvalidOperationException("The selected primary display is unavailable.");
    }

    private static void Query(out NativeMethods.DISPLAYCONFIG_PATH_INFO[] paths,
        out NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes)
    {
        uint flags = NativeMethods.QDC_ALL_PATHS | NativeMethods.QDC_VIRTUAL_MODE_AWARE;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int error = NativeMethods.GetDisplayConfigBufferSizes(flags, out uint pathCount, out uint modeCount);
            if (error != 0) throw new Win32Exception(error);
            paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
            modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];
            error = NativeMethods.QueryDisplayConfig(flags, ref pathCount, paths, ref modeCount, modes, nint.Zero);
            if (error == 0)
            {
                Array.Resize(ref paths, (int)pathCount);
                Array.Resize(ref modes, (int)modeCount);
                return;
            }
            if (error != 122) throw new Win32Exception(error);
        }
        throw new Win32Exception(122);
    }

    private static bool IsPrimary(NativeMethods.DISPLAYCONFIG_PATH_INFO path,
        NativeMethods.DISPLAYCONFIG_MODE_INFO[] modes)
    {
        if (path.sourceInfo.modeInfoIdx == NativeMethods.DISPLAYCONFIG_PATH_MODE_IDX_INVALID)
            return false;
        NativeMethods.DISPLAYCONFIG_MODE_INFO mode = modes[path.sourceInfo.modeInfoIdx];
        return mode.infoType == NativeMethods.DISPLAYCONFIG_MODE_INFO_TYPE.Source &&
               mode.sourceMode.position.x == 0 && mode.sourceMode.position.y == 0;
    }

    private static string GetFriendlyName(NativeMethods.LUID adapterId, uint targetId)
    {
        var name = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = NativeMethods.DeviceInfoHeader(
                NativeMethods.DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName,
                Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>(), adapterId, targetId)
        };
        return NativeMethods.DisplayConfigGetDeviceInfo(ref name) == 0 &&
               !string.IsNullOrWhiteSpace(name.monitorFriendlyDeviceName)
            ? name.monitorFriendlyDeviceName
            : $"Display {targetId + 1}";
    }

    private static string GetMonitorPath(NativeMethods.LUID adapterId, uint targetId)
    {
        var name = new NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME
        {
            header = NativeMethods.DeviceInfoHeader(
                NativeMethods.DISPLAYCONFIG_DEVICE_INFO_TYPE.GetTargetName,
                Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_TARGET_DEVICE_NAME>(), adapterId, targetId)
        };
        return NativeMethods.DisplayConfigGetDeviceInfo(ref name) == 0
            ? name.monitorDevicePath
            : string.Empty;
    }

    private static string GetGdiName(NativeMethods.LUID adapterId, uint targetId)
    {
        var source = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME
        {
            header = NativeMethods.DeviceInfoHeader(
                NativeMethods.DISPLAYCONFIG_DEVICE_INFO_TYPE.GetSourceName,
                Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(), adapterId, targetId)
        };
        return NativeMethods.DisplayConfigGetDeviceInfo(ref source) == 0 ? source.viewGdiDeviceName : string.Empty;
    }
}
