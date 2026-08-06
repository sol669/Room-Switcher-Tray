using RoomSwitcherTray.Models;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDevice> GetRenderDevices()
    {
        var result = new List<AudioDevice>();
        IMMDeviceEnumerator enumerator =
            (IMMDeviceEnumerator)(object)new MMDeviceEnumeratorComObject();
        enumerator.EnumAudioEndpoints(EDataFlow.eRender, DeviceState.Active, out IMMDeviceCollection collection);
        enumerator.GetDefaultAudioEndpoint(EDataFlow.eRender, ERole.eConsole, out IMMDevice defaultDevice);
        defaultDevice.GetId(out string defaultId);
        collection.GetCount(out uint count);

        for (uint i = 0; i < count; i++)
        {
            collection.Item(i, out IMMDevice device);
            device.GetId(out string id);
            device.OpenPropertyStore(0, out IPropertyStore store);
            var key = PropertyKeys.PKEY_Device_FriendlyName;
            store.GetValue(ref key, out PropVariant value);
            string name = value.GetString() ?? id;
            value.Clear();
            result.Add(new AudioDevice(id, name, id == defaultId, GetVolumePercent(device)));
        }

        return result.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
    }

    private static int GetVolumePercent(IMMDevice device)
    {
        try
        {
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            int hr = device.Activate(ref iid, 23, nint.Zero, out object instance);
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
            var volume = (IAudioEndpointVolume)instance;
            hr = volume.GetMasterVolumeLevelScalar(out float scalar);
            if (hr != 0)
                Marshal.ThrowExceptionForHR(hr);
            return Math.Clamp((int)Math.Round(scalar * 100), 0, 100);
        }
        catch
        {
            return 0;
        }
    }

    public bool SetDefault(string deviceId, out string? error)
    {
        error = null;
        try
        {
            IPolicyConfig policy = (IPolicyConfig)(object)new PolicyConfigClient();
            foreach (ERole role in Enum.GetValues<ERole>())
            {
                int hr = policy.SetDefaultEndpoint(deviceId, role);
                if (hr != 0)
                    Marshal.ThrowExceptionForHR(hr);
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            SettingsStore.Log(ex);
            return false;
        }
    }

    private enum EDataFlow { eRender, eCapture, eAll }
    private enum ERole { eConsole, eMultimedia, eCommunications }

    [Flags]
    private enum DeviceState : uint
    {
        Active = 0x1
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class MMDeviceEnumeratorComObject { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(EDataFlow dataFlow, DeviceState stateMask, out IMMDeviceCollection devices);
        [PreserveSig]
        int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice endpoint);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("0BD7A1BE-7A1A-44DB-8397-C0A9B85B3E2D")]
    private interface IMMDeviceCollection
    {
        [PreserveSig]
        int GetCount(out uint count);
        [PreserveSig]
        int Item(uint index, out IMMDevice device);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    private interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, uint clsCtx, nint activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
        [PreserveSig]
        int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig]
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig]
        int GetState(out DeviceState state);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(nint notify);
        [PreserveSig] int UnregisterControlChangeNotify(nint notify);
        [PreserveSig] int GetChannelCount(out uint channelCount);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, nint context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, nint context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, nint context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, nint context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, nint context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint propertyCount);
        [PreserveSig]
        int GetAt(uint propertyIndex, out PropertyKey key);
        [PreserveSig]
        int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    private static class PropertyKeys
    {
        public static PropertyKey PKEY_Device_FriendlyName = new()
        {
            FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"),
            PropertyId = 14
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PropVariant
    {
        [FieldOffset(0)] private ushort _variantType;
        [FieldOffset(8)] private nint _pointerValue;

        public string? GetString() => _variantType == 31
            ? Marshal.PtrToStringUni(_pointerValue)
            : null;

        public void Clear() => PropVariantClear(ref this);
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant value);

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private sealed class PolicyConfigClient { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
    private interface IPolicyConfig
    {
        int GetMixFormat();
        int GetDeviceFormat();
        int ResetDeviceFormat();
        int SetDeviceFormat();
        int GetProcessingPeriod();
        int SetProcessingPeriod();
        int GetShareMode();
        int SetShareMode();
        int GetPropertyValue();
        int SetPropertyValue();
        [PreserveSig]
        int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
        int SetEndpointVisibility();
    }
}

