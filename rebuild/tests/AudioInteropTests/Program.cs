using RoomSwitcherTray.Core.Services;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

// Read-only integration test of the production COM code, with no app, settings,
// tray window, audio setters, display setters or persistent log files.
internal static class Program
{
    private static int reads;
    private static int callbacks;

    private static void ReadAudio()
    {
        var endpoints = AudioService.GetRenderDevices();
        if (endpoints.Any(item => string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name)))
            throw new Exception("An endpoint lost its ID or system name.");
        _ = new AudioService().GetDefaultEndpointStatus(endpoints);
        Interlocked.Increment(ref reads);
    }

    [STAThread]
    private static int Main(string[] args)
    {
        object? foreign = null;
        try
        {
            AudioNamingTests.Run();
            if (args.Contains("--policy-only")) return 0;
            if (args.Contains("--foreign-first")) foreign = new ForeignEnumerator();
            Run(args);
            if (foreign is not null)
            {
                // A caller outside our service must retain a usable COM reference too.
                nint identity = Marshal.GetIUnknownForObject(foreign);
                Marshal.Release(identity);
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { CoreAudioInterop.Release(foreign); }
    }

    private static void Run(string[] args)
    {
        // Run each order in a fresh process so the first COM activation is real.
        bool readerFirst = args.Contains("--reader-first");
        if (readerFirst) ReadAudio();
        using (var watcher = new AudioDeviceWatcher(() => Interlocked.Increment(ref callbacks)))
        {
            ReadAudio(); // This line reproduces 0.11.0 when watcher is created first.
            var workers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                for (int i = 0; i < 20; i++) ReadAudio();
            })).ToArray();
            // Read on the STA caller while MTA workers enumerate in parallel.
            for (int i = 0; i < 20; i++)
            {
                using var temporaryWatcher = new AudioDeviceWatcher(() => Interlocked.Increment(ref callbacks));
                ReadAudio();
            }
            Task.WaitAll(workers);
            ReadAudio(); // Short-lived reads/watchers must not invalidate the long-lived watcher.
        }
        using (var survivingWatcher = new AudioDeviceWatcher(() => Interlocked.Increment(ref callbacks)))
        {
            using (var otherWatcher = new AudioDeviceWatcher(() => Interlocked.Increment(ref callbacks))) ReadAudio();
            ReadAudio(); // Disposing one observer must not break another observer or reader.
        }
        ReadAudio(); // Unregistration must not poison later COM activation.
        var first = CoreAudioInterop.CreateEnumerator();
        var second = CoreAudioInterop.CreateEnumerator();
        try
        {
            if (ReferenceEquals(first, second)) throw new Exception("Independent owners received the same COM wrapper.");
            CoreAudioInterop.Release(first);
            first = null;
            Marshal.ThrowExceptionForHR(second.EnumAudioEndpoints(CoreAudioInterop.EDataFlow.Render, 15, out var collection));
            CoreAudioInterop.Release(collection);
        }
        finally { CoreAudioInterop.Release(first); CoreAudioInterop.Release(second); }
        if (!SettingsStore.Errors.IsEmpty)
            throw new AggregateException("Audio service suppressed integration errors.", SettingsStore.Errors);
        string order = args.Contains("--foreign-first") ? "foreign-coclass-first" : readerFirst ? "reader-first" : "watcher-first";
        Console.WriteLine($"PASS: {order}, {reads} reads, STA + 4 MTA workers, independent COM ownership, observer lifetime overlap, {callbacks} notifications. No device configuration or settings changed.");
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private sealed class ForeignEnumerator { }
}

namespace RoomSwitcherTray.Core.Services
{
    internal static class SettingsStore
    {
        public static readonly ConcurrentQueue<Exception> Errors = new();
        public static void Log(Exception error) => Errors.Enqueue(error);
    }
}
