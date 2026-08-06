using RoomSwitcherTray.Models;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Services;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDevice> GetRenderDevices()
    {
        // Keep native Core Audio discovery outside the stable startup path.
        // Certain drivers currently terminate the process inside the COM proxy
        // before a managed exception can be handled. Audio discovery will be
        // restored behind an isolated broker after the tray/display baseline
        // is verified on real hardware.
        return Array.Empty<AudioDevice>();
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
