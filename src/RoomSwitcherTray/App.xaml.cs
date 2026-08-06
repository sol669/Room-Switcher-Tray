using Microsoft.UI.Xaml;
using RoomSwitcherTray.Services;

namespace RoomSwitcherTray;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Window? _lifetimeWindow;

    internal static SettingsStore Settings { get; } = new();
    internal static DisplayService Displays { get; } = new();
    internal static AudioDeviceService Audio { get; } = new();
    internal static ScenarioService Scenarios { get; } = new(Settings, Displays, Audio);
    internal static TrayService? Tray { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            SettingsStore.Log(e.Exception);
            e.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, "sol669.RoomSwitcherTray.Singleton", out bool createdNew);
        if (!createdNew)
        {
            Exit();
            return;
        }

        Settings.Load();
        CreateLifetimeWindow();
        Tray = new TrayService(Settings, Scenarios);
        Tray.Initialize();

        // Settings are opened explicitly from the tray. Keeping startup window-free
        // isolates the native tray lifetime from WinUI window construction.
    }

    private void CreateLifetimeWindow()
    {
        _lifetimeWindow = new Window();
        _lifetimeWindow.AppWindow.IsShownInSwitchers = false;
        _lifetimeWindow.AppWindow.Move(new Windows.Graphics.PointInt32(-32000, -32000));
        _lifetimeWindow.Activate();
        _lifetimeWindow.AppWindow.Hide();
    }

    internal static void Quit()
    {
        Tray?.Dispose();
        Current.Exit();
    }
}
