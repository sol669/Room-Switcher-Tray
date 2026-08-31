using System.Runtime.InteropServices;
using static RoomSwitcherTray.Core.Services.CoreAudioInterop;

namespace RoomSwitcherTray.Core.Services;

/// <summary>Callbacks only enqueue work; never enumerate or block the native callback thread.</summary>
public sealed class AudioDeviceWatcher : IDisposable
{
    private IMMDeviceEnumerator? _enumerator;
    private NotificationSink? _sink;
    private bool _registered;
    public AudioDeviceWatcher(Action changed)
    {
        try
        {
            _sink = new NotificationSink(changed);
            _enumerator = CreateEnumerator();
            Marshal.ThrowExceptionForHR(_enumerator.RegisterEndpointNotificationCallback(_sink));
            _registered = true;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (_sink is not null) _sink.Stop();
        if (_enumerator is not null)
        {
            try
            {
                if (_registered && _sink is not null)
                    Marshal.ThrowExceptionForHR(_enumerator.UnregisterEndpointNotificationCallback(_sink));
            }
            catch (Exception ex) { SettingsStore.Log(ex); }
            finally { Release(_enumerator); _enumerator = null; _registered = false; }
        }
        _sink = null;
    }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class NotificationSink(Action changed) : IAudioNotificationClient
    {
        private Action? _changed = changed;
        public void Stop() => Interlocked.Exchange(ref _changed, null);
        private int Notify()
        {
            try { Volatile.Read(ref _changed)?.Invoke(); }
            catch { }
            return 0;
        }
        public int OnDeviceStateChanged(string id, uint state) => Notify();
        public int OnDeviceAdded(string id) => Notify();
        public int OnDeviceRemoved(string id) => Notify();
        public int OnDefaultDeviceChanged(int flow, int role, string? id) => flow == 0 ? Notify() : 0;
        public int OnPropertyValueChanged(string id, AudioPropertyKey key) => Notify();
    }
}
