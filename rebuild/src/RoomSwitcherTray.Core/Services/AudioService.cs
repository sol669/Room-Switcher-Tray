using System.Runtime.InteropServices;
using Windows.Devices.Enumeration;
using Windows.Media.Devices;

namespace RoomSwitcherTray.Core.Services;

public sealed class AudioService
{
    public async Task<IReadOnlyList<AudioDevice>> GetRenderDevicesAsync()
    {
        string selector = MediaDevice.GetAudioRenderSelector();
        DeviceInformationCollection devices = await DeviceInformation.FindAllAsync(selector);
        string defaultId = MediaDevice.GetDefaultAudioRenderId(AudioDeviceRole.Default);
        return devices
            .Where(device => device.IsEnabled)
            .Select(device => new AudioDevice(device.Id, device.Name,
                device.Id.Equals(defaultId, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(device => device.IsDefault)
            .ThenBy(device => device.Name)
            .ToList();
    }

    public async Task SetDefaultWhenAvailableAsync(string deviceId, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<AudioDevice> devices = await GetRenderDevicesAsync();
            if (devices.Any(device => device.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase)))
            {
                SetDefault(deviceId);
                return;
            }
            await Task.Delay(250, cancellationToken);
        }
        throw new InvalidOperationException("Выбранное аудиоустройство не появилось после переключения дисплея.");
    }

    private static void SetDefault(string deviceId)
    {
        IPolicyConfig policy = (IPolicyConfig)(object)new PolicyConfigClient();
        try
        {
            ThrowIfFailed(policy.SetDefaultEndpoint(deviceId, ERole.Console));
            ThrowIfFailed(policy.SetDefaultEndpoint(deviceId, ERole.Multimedia));
        }
        finally
        {
            if (Marshal.IsComObject(policy))
                Marshal.FinalReleaseComObject(policy);
        }
    }

    private static void ThrowIfFailed(int result)
    {
        if (result < 0)
            Marshal.ThrowExceptionForHR(result);
    }

    private enum ERole { Console, Multimedia, Communications }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private sealed class PolicyConfigClient { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown),
     Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
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
}
