using Microsoft.UI.Xaml;
using RoomSwitcherTray.Core.Services;

namespace RoomSwitcherTray.Core;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private Window? _lifetimeWindow;

    internal static SettingsStore Settings { get; } = new();
    internal static DisplayService Displays { get; } = new();
    internal static AudioService Audio { get; } = new();
    internal static ScenarioService Scenarios { get; } = new(Settings, Displays, Audio);
    internal static TrayService? Tray { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, args) =>
        {
            SettingsStore.Log(args.Exception);
            args.Handled = true;
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _singleInstance = new Mutex(true, "sol669.RoomSwitcherTray.Core.Singleton", out bool created);
        if (!created)
        {
            Exit();
            return;
        }

        Settings.Load();
        CreateLifetimeWindow();
        Tray = new TrayService(Settings, Scenarios);
        Tray.Initialize();
        ApplyStartupScenario();

        // Настройки намеренно не открываются автоматически. Трей должен
        // продолжать работать, даже если окно настройки не удалось создать.
    }

    private static void ApplyStartupScenario()
    {
        Guid? scenarioId = Settings.Current.StartupScenarioMode switch
        {
            StartupScenarioMode.RestoreLastScenario => Settings.Current.ActiveScenarioId,
            StartupScenarioMode.AlwaysUseScenario => Settings.Current.StartupScenarioId,
            _ => null
        };
        if (scenarioId is Guid id && Settings.Current.Scenarios.Any(scenario => scenario.Id == id))
            _ = Tray?.ApplyScenarioAsync(id);
    }

    private void CreateLifetimeWindow()
    {
        _lifetimeWindow = new Window();
        _lifetimeWindow.AppWindow.IsShownInSwitchers = false;
        _lifetimeWindow.Activate();
        _lifetimeWindow.AppWindow.Hide();
    }

    internal static void Quit()
    {
        Tray?.Dispose();
        Current.Exit();
    }
}
