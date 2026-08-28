using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core.Services;

internal static class DeviceIdentityService
{
    private static readonly DEVPROPKEY ContainerIdKey = new()
    {
        fmtid = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"),
        pid = 2
    };

    internal static Guid? GetContainerId(string deviceInterfacePath)
    {
        if (string.IsNullOrWhiteSpace(deviceInterfacePath)) return null;

        nint deviceInfoSet = SetupDiCreateDeviceInfoList(nint.Zero, nint.Zero);
        if (deviceInfoSet == new nint(-1)) return null;
        try
        {
            var interfaceData = new SP_DEVICE_INTERFACE_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>()
            };
            if (!SetupDiOpenDeviceInterface(deviceInfoSet, deviceInterfacePath, 0, ref interfaceData))
                return null;

            var deviceInfoData = new SP_DEVINFO_DATA
            {
                cbSize = (uint)Marshal.SizeOf<SP_DEVINFO_DATA>()
            };
            SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, nint.Zero, 0,
                out uint requiredSize, ref deviceInfoData);
            if (requiredSize == 0) return null;

            nint detail = Marshal.AllocHGlobal((int)requiredSize);
            try
            {
                Marshal.WriteInt32(detail, nint.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detail,
                    requiredSize, out _, ref deviceInfoData))
                    return null;

                var value = new byte[16];
                DEVPROPKEY key = ContainerIdKey;
                if (!SetupDiGetDeviceProperty(deviceInfoSet, ref deviceInfoData, ref key,
                    out _, value, (uint)value.Length, out uint valueSize, 0) || valueSize != 16)
                    return null;
                return new Guid(value);
            }
            finally
            {
                Marshal.FreeHGlobal(detail);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA
    {
        public uint cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public nuint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public nuint Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern nint SetupDiCreateDeviceInfoList(nint classGuid, nint parentWindow);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiOpenDeviceInterface(nint deviceInfoSet, string devicePath,
        uint openFlags, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet,
        ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, nint detailData, uint detailDataSize,
        out uint requiredSize, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceProperty(nint deviceInfoSet,
        ref SP_DEVINFO_DATA deviceInfoData, ref DEVPROPKEY propertyKey, out uint propertyType,
        [Out] byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize, uint flags);

    [DllImport("setupapi.dll")]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}
