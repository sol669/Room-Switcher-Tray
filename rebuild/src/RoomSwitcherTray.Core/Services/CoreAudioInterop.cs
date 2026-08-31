using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core.Services;

/// <summary>One ABI definition and ownership policy for Core Audio readers and notifications.</summary>
internal static class CoreAudioInterop
{
    private static readonly Guid EnumeratorClassId = new("BCDE0395-E52F-467C-8E3D-C4579291692E");

    public static IMMDeviceEnumerator CreateEnumerator()
    {
        // COM can return the same native identity for independent callers. Do not cast
        // it to a managed coclass or let a temporary read release a watcher's wrapper.
        // CLR activation also initializes COM for the calling STA/MTA thread.
        object activation = Activator.CreateInstance(Type.GetTypeFromCLSID(EnumeratorClassId, true)!)!;
        nint unknown = nint.Zero;
        object? owned = null;
        try
        {
            unknown = Marshal.GetIUnknownForObject(activation);
            owned = Marshal.GetUniqueObjectForIUnknown(unknown);
            return (IMMDeviceEnumerator)owned;
        }
        catch { Release(owned); throw; }
        finally
        {
            if (unknown != nint.Zero) Marshal.Release(unknown);
            Release(activation);
        }
    }

    public static void Release(object? value)
    {
        // Balance only this acquisition. Device/property wrappers can be shared with
        // concurrent reads. Never force their reference counts to zero.
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }

    internal enum EDataFlow { Render, Capture, All }
    internal enum ERole { Console, Multimedia, Communications }

    // PROPVARIANT is 24 bytes on x64, including its largest union member.
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public nint pointerValue;
        [FieldOffset(8)] public uint uintValue;
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(EDataFlow flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(EDataFlow flow, ERole role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IAudioNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IAudioNotificationClient client);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, nint activationParams, out nint instance);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out AudioPropertyKey key);
        [PreserveSig] int GetValue(ref AudioPropertyKey key, out PROPVARIANT value);
        [PreserveSig] int SetValue(ref AudioPropertyKey key, ref PROPVARIANT value);
        [PreserveSig] int Commit();
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct AudioPropertyKey { public Guid fmtid; public uint pid; }

[ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IAudioNotificationClient
{
    [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
    [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
    [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string? id);
    [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, AudioPropertyKey key);
}
