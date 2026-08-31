using System.Runtime.InteropServices;
using static RoomSwitcherTray.Core.Services.CoreAudioInterop;
using PROPERTYKEY = RoomSwitcherTray.Core.Services.AudioPropertyKey;

namespace RoomSwitcherTray.Core.Services;

/// <summary>
/// Enumerates Core Audio endpoints in every state. This is important for HDMI
/// and DisplayPort audio: the endpoint can be unplugged/not-present while its
/// display is disabled, but it still has to be selectable in a saved scenario.
/// </summary>
public sealed class AudioService
{
    private const uint DeviceStateActive = 0x00000001;
    private const uint DeviceStateDisabled = 0x00000002;
    private const uint DeviceStateNotPresent = 0x00000004;
    private const uint DeviceStateUnplugged = 0x00000008;
    private const uint DeviceStateMaskAll = 0x0000000F;
    private const uint StgmRead = 0;

    private static readonly PROPERTYKEY FriendlyNameKey = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 14
    };

    private static readonly PROPERTYKEY InterfaceFriendlyNameKey = new()
    {
        fmtid = new Guid("026E516E-B814-414B-83CD-856D6FEF4822"),
        pid = 2
    };

    private static readonly PROPERTYKEY DeviceDescriptionKey = new()
    {
        fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
        pid = 2
    };

    private static readonly PROPERTYKEY ContainerIdKey = new()
    {
        fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        pid = 2
    };

    private static readonly PROPERTYKEY FormFactorKey = new()
    {
        fmtid = new Guid("1DA5D803-D492-4EDD-8C23-E0C0FFEE7F0E"),
        pid = 0
    };

    public Task<IReadOnlyList<AudioDevice>> GetRenderDevicesAsync()
    {
        return Task.FromResult<IReadOnlyList<AudioDevice>>(GetRenderDevices());
    }

    public AudioEndpointStatus? GetDefaultEndpointStatus() => GetDefaultEndpointStatus(GetRenderDevices());

    public AudioEndpointStatus? GetDefaultEndpointStatus(IReadOnlyList<AudioDevice> devices)
    {
        AudioDevice? device = devices.FirstOrDefault(item => item.IsDefault && item.IsActive);
        if (device is null) return null;
        return GetEndpointStatus(device);
    }

    public static AudioEndpointStatus GetEndpointStatus(AudioDevice device)
    {
        IAudioEndpointVolume? volume = null;
        try
        {
            volume = ActivateEndpointVolume(device.Id);
            ThrowIfFailed(volume.GetMasterVolumeLevelScalar(out float level));
            ThrowIfFailed(volume.GetMute(out bool muted));
            int percent = Math.Clamp((int)Math.Round(level * 100), 0, 100);
            string name = string.IsNullOrWhiteSpace(device.DisplayName)
                ? device.Name
                : $"{device.DisplayName} — HDMI/DisplayPort";
            return new AudioEndpointStatus(name, percent, muted);
        }
        finally { Release(volume); }
    }

    public string? GetDefaultEndpointId() => GetRenderDevices()
        .FirstOrDefault(item => item.IsDefault && item.IsActive)?.Id;

    public void SetDefaultEndpointMuted(bool muted)
    {
        string? deviceId = GetRenderDevices().FirstOrDefault(item => item.IsDefault && item.IsActive)?.Id;
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new InvalidOperationException("Активное аудиоустройство не найдено.");
        SetEndpointMuted(deviceId, muted);
    }

    public static void SetEndpointMuted(string deviceId, bool muted)
    {
        IAudioEndpointVolume? volume = null;
        try
        {
            volume = ActivateEndpointVolume(deviceId);
            Guid context = Guid.Empty;
            ThrowIfFailed(volume.SetMute(muted, ref context));
        }
        finally { Release(volume); }
    }

    public void SetDefaultEndpointVolume(int percent)
    {
        string? deviceId = GetRenderDevices().FirstOrDefault(item => item.IsDefault && item.IsActive)?.Id;
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new InvalidOperationException("Активное аудиоустройство не найдено.");
        SetEndpointVolume(deviceId, percent);
    }

    public static void SetEndpointVolume(string deviceId, int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        IAudioEndpointVolume? volume = null;
        try
        {
            volume = ActivateEndpointVolume(deviceId);
            Guid context = Guid.Empty;
            if (percent == 0)
            {
                ThrowIfFailed(volume.SetMute(true, ref context));
                return;
            }
            ThrowIfFailed(volume.SetMasterVolumeLevelScalar(percent / 100f, ref context));
            ThrowIfFailed(volume.SetMute(false, ref context));
        }
        finally { Release(volume); }
    }

    public Task<IReadOnlyList<AudioDevice>> GetVisibleRenderDevicesAsync(
        IReadOnlyCollection<DisplayDevice> displays,
        params ScenarioDefinition?[] scenarios)
        => Task.FromResult(GetVisibleRenderDevices(GetRenderDevices(), displays, scenarios));

    public static IReadOnlyList<AudioDevice> GetVisibleRenderDevices(
        IReadOnlyList<AudioDevice> all, IReadOnlyCollection<DisplayDevice> displays,
        params ScenarioDefinition?[] scenarios)
    {
        Dictionary<Guid, string> displayNames = displays
            .Where(display => DeviceIdentity.IsDeviceContainer(display.ContainerId))
            .GroupBy(display => display.ContainerId!.Value)
            .Where(group => group.Select(display => display.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1)
            .Where(group => all.Where(device => device.ContainerId == group.Key).All(device => device.Kind == AudioDeviceKind.Display))
            .ToDictionary(group => group.Key, group => group.First().Name);
        var savedIds = scenarios
            .Where(scenario => scenario is not null)
            .Select(scenario => scenario!.AudioDeviceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var savedContainers = scenarios
            .Where(scenario => scenario is not null)
            .Select(scenario => ParseContainerId(scenario!.AudioDeviceContainerId))
            .Where(id => DeviceIdentity.IsDeviceContainer(id))
            .Select(id => id!.Value)
            .ToHashSet();
        foreach (AudioDevice savedDevice in all.Where(device => savedIds.Contains(device.Id)))
        {
            if (DeviceIdentity.IsDeviceContainer(savedDevice.ContainerId))
                savedContainers.Add(savedDevice.ContainerId!.Value);
        }

        IEnumerable<AudioDevice> identified = all
            .Select(device => device.Kind == AudioDeviceKind.Display && device.ContainerId is Guid containerId &&
                displayNames.TryGetValue(containerId, out string? displayName)
                    ? device with { DisplayName = displayName }
                    : device with { DisplayName = null });
        IReadOnlyList<AudioDevice> visible = identified
            .GroupBy(device => device.Kind == AudioDeviceKind.Display && DeviceIdentity.IsDeviceContainer(device.ContainerId) &&
                displayNames.ContainsKey(device.ContainerId!.Value) &&
                !savedIds.Contains(device.Id) &&
                all.Count(other => other.ContainerId == device.ContainerId && other.IsActive) <= 1 &&
                all.Where(other => other.ContainerId == device.ContainerId).All(other => other.Kind == AudioDeviceKind.Display)
                ? $"display:{device.ContainerId!.Value:D}"
                : $"endpoint:{device.Id}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(device => device.IsActive)
                .ThenByDescending(device => savedIds.Contains(device.Id))
                .ThenByDescending(device => !string.IsNullOrWhiteSpace(device.DisplayName) &&
                    device.Name.Contains(device.DisplayName,
                        StringComparison.CurrentCultureIgnoreCase))
                .ThenBy(device => StateOrder(device.State))
                .First())
            .Where(device => IsVisible(device, displayNames.Keys, savedIds, savedContainers))
            .OrderByDescending(device => device.IsDefault)
            .ThenByDescending(device => device.IsActive)
            .ThenBy(device => device.DisplayName ?? device.Name,
                StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return visible;
    }

    private static int StateOrder(AudioDeviceState state) => state switch
    {
        AudioDeviceState.Active => 0,
        AudioDeviceState.Disabled => 1,
        AudioDeviceState.Unplugged => 2,
        _ => 3
    };

    private static bool IsVisible(
        AudioDevice device,
        ICollection<Guid> displayContainers,
        ISet<string> savedIds,
        ISet<Guid> savedContainers)
    {
        bool saved = savedIds.Contains(device.Id) ||
            device.Kind == AudioDeviceKind.Display &&
            device.ContainerId is Guid savedContainer && savedContainers.Contains(savedContainer);
        if (saved) return true;

        if (device.Kind == AudioDeviceKind.Display)
            return device.IsActive ||
                device.ContainerId is Guid displayContainer &&
                displayContainers.Contains(displayContainer);

        return device.State != AudioDeviceState.NotPresent;
    }

    public static IReadOnlyList<AudioDevice> GetRenderDevices()
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDeviceCollection? collection = null;
        try
        {
            enumerator = CreateEnumerator();
            string defaultId = GetDefaultId(enumerator);
            ThrowIfFailed(enumerator.EnumAudioEndpoints(EDataFlow.Render, DeviceStateMaskAll, out collection));
            ThrowIfFailed(collection.GetCount(out uint count));

            var result = new List<AudioDevice>((int)count);
            for (uint index = 0; index < count; index++)
            {
                IMMDevice? device = null;
                try
                {
                    ThrowIfFailed(collection.Item(index, out device));
                    ThrowIfFailed(device.GetId(out string id));
                    ThrowIfFailed(device.GetState(out uint state));
                    string name = GetFriendlyName(device);
                    if (string.IsNullOrWhiteSpace(name)) name = id;
                    result.Add(new AudioDevice(
                        id,
                        name,
                        id.Equals(defaultId, StringComparison.OrdinalIgnoreCase),
                        ConvertState(state),
                        GetContainerId(device),
                        GetKind(device)));
                }
                catch (Exception ex)
                {
                    // A stale endpoint can disappear while Windows is enumerating
                    // devices. Keep the remaining endpoints available in the UI.
                    SettingsStore.Log(ex);
                }
                finally
                {
                    Release(device);
                }
            }

            return result
                .OrderByDescending(device => device.IsDefault)
                .ThenByDescending(device => device.IsActive)
                .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        finally
        {
            Release(collection);
            Release(enumerator);
        }
    }


    private static Guid? ParseContainerId(string? value) =>
        Guid.TryParse(value, out Guid result) ? result : null;

    private static string GetDefaultId(IMMDeviceEnumerator enumerator)
    {
        IMMDevice? device = null;
        try
        {
            int result = enumerator.GetDefaultAudioEndpoint(EDataFlow.Render, ERole.Multimedia, out device);
            if (result < 0 || device is null) return string.Empty;
            return device.GetId(out string id) >= 0 ? id : string.Empty;
        }
        finally
        {
            Release(device);
        }
    }

    private static string GetFriendlyName(IMMDevice device)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(StgmRead, out store) < 0 || store is null)
                return string.Empty;

            foreach (PROPERTYKEY candidate in new[]
                     {
                         FriendlyNameKey,
                         InterfaceFriendlyNameKey,
                         DeviceDescriptionKey
                     })
            {
                PROPERTYKEY key = candidate;
                PROPVARIANT value = default;
                try
                {
                    // Some disconnected HDMI endpoints expose no friendly-name
                    // property and return a driver-specific "file not found" HRESULT.
                    // The endpoint itself is still valid and must remain selectable.
                    if (store.GetValue(ref key, out value) >= 0 &&
                        value.vt == 31 && value.pointerValue != nint.Zero)
                    {
                        string? name = Marshal.PtrToStringUni(value.pointerValue);
                        if (!string.IsNullOrWhiteSpace(name)) return name;
                    }
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }

            return string.Empty;
        }
        finally
        {
            Release(store);
        }
    }

    private static Guid? GetContainerId(IMMDevice device)
    {
        PROPVARIANT value = GetProperty(device, ContainerIdKey);
        try
        {
            return value.vt == 72 && value.pointerValue != nint.Zero
                ? Marshal.PtrToStructure<Guid>(value.pointerValue)
                : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static AudioDeviceKind GetKind(IMMDevice device)
    {
        PROPVARIANT value = GetProperty(device, FormFactorKey);
        try
        {
            return value.vt == 19 && value.uintValue == 9
                ? AudioDeviceKind.Display
                : AudioDeviceKind.Other;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    private static PROPVARIANT GetProperty(IMMDevice device, PROPERTYKEY propertyKey)
    {
        IPropertyStore? store = null;
        try
        {
            if (device.OpenPropertyStore(StgmRead, out store) < 0 || store is null)
                return default;
            PROPERTYKEY key = propertyKey;
            return store.GetValue(ref key, out PROPVARIANT value) >= 0 ? value : default;
        }
        finally
        {
            Release(store);
        }
    }

    private static AudioDeviceState ConvertState(uint state) => state switch
    {
        DeviceStateActive => AudioDeviceState.Active,
        DeviceStateDisabled => AudioDeviceState.Disabled,
        DeviceStateUnplugged => AudioDeviceState.Unplugged,
        _ => AudioDeviceState.NotPresent
    };

    public static void SetDefault(string deviceId)
    {
        IPolicyConfig policy = (IPolicyConfig)(object)new PolicyConfigClient();
        try
        {
            ThrowIfFailed(policy.SetDefaultEndpoint(deviceId, ERole.Console));
            ThrowIfFailed(policy.SetDefaultEndpoint(deviceId, ERole.Multimedia));
        }
        finally
        {
            Release(policy);
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }

    private static IAudioEndpointVolume ActivateEndpointVolume(string deviceId)
    {
        IMMDeviceEnumerator? enumerator = null;
        IMMDevice? device = null;
        try
        {
            enumerator = CreateEnumerator();
            ThrowIfFailed(enumerator.GetDevice(deviceId, out device));
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            ThrowIfFailed(device.Activate(ref iid, 23, nint.Zero, out nint instance));
            try { return (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(instance); }
            finally { Marshal.Release(instance); }
        }
        finally
        {
            Release(device);
            Release(enumerator);
        }
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid eventContext);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid eventContext);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid eventContext);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid eventContext);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid eventContext);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
        [PreserveSig] int GetVolumeStepInfo(out uint step, out uint stepCount);
        [PreserveSig] int VolumeStepUp(ref Guid eventContext);
        [PreserveSig] int VolumeStepDown(ref Guid eventContext);
        [PreserveSig] int QueryHardwareSupport(out uint hardwareSupportMask);
        [PreserveSig] int GetVolumeRange(out float minDb, out float maxDb, out float incrementDb);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private sealed class PolicyConfigClient { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    private interface IPolicyConfig
    {
        int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string device, nint format);
        int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, int isDefault, nint format);
        int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device);
        int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string device, nint endpointFormat, nint mixFormat);
        int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, int isDefault, nint defaultPeriod, nint minimumPeriod);
        int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string device, nint period);
        int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, nint mode);
        int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string device, nint mode);
        int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, nint key, nint value);
        int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string device, nint key, nint value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, ERole role);
        int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string device, int visible);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PROPVARIANT value);
}
