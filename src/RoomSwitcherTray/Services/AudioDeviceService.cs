using RoomSwitcherTray.Models;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace RoomSwitcherTray.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDevice> GetRenderDevices()
    {
        using var enumerator = new NAudio.CoreAudioApi.MMDeviceEnumerator();
        using NAudio.CoreAudioApi.MMDevice defaultDevice =
            enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        string defaultId = defaultDevice.ID;
        using MMDeviceCollection collection = enumerator.EnumerateAudioEndPoints(
            DataFlow.Render, NAudio.CoreAudioApi.DeviceState.Active);
        var result = new List<AudioDevice>(collection.Count);

        foreach (NAudio.CoreAudioApi.MMDevice device in collection)
        {
            int volume = Math.Clamp((int)Math.Round(
                device.AudioEndpointVolume.MasterVolumeLevelScalar * 100), 0, 100);
            result.Add(new AudioDevice(device.ID, device.FriendlyName,
                device.ID == defaultId, volume));
        }

        return result.OrderByDescending(d => d.IsDefault).ThenBy(d => d.Name).ToList();
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

    private enum ERole { eConsole, eMultimedia, eCommunications }

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

