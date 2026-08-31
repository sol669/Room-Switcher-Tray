using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using RoomSwitcherTray.Core.Services;
using System.Reflection;
using System.Text.Json;

namespace RoomSwitcherTray.Core
{
    public partial class App : Application
    {
        internal static SettingsStore Settings { get; } = new();
        internal static DisplayService Displays { get; } = new();
        internal static AudioService Audio { get; } = new();
        internal static ScenarioService Scenarios { get; } = new(Settings, Displays, Audio);
        public App() { InitializeComponent(); UnhandledException += (_, e) => { SettingsStore.Log(e.Exception); e.Handled = true; }; }
        internal static void Quit() => Current.Exit();
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            // No mutex, tray initialization, startup scenario, real persistence or autostart writes.
            var window = new WinUiSettingsWindow(Settings, new TrayService(Settings, Scenarios));
            window.Activate();
            window.DispatcherQueue.TryEnqueue(async () =>
            {
                bool success = false;
                try
                {
                    await Task.Delay(500);
                    const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                    var type = typeof(WinUiSettingsWindow);
                    foreach (var (language, theme, label) in new[] {
                        (AppLanguage.English, AppThemeMode.Light, "Settings"),
                        (AppLanguage.Russian, AppThemeMode.Dark, "Настройки"),
                        (AppLanguage.English, AppThemeMode.Dark, "Settings") })
                    {
                        type.GetField("_pendingLanguage", flags)!.SetValue(window, language);
                        type.GetField("_pendingTheme", flags)!.SetValue(window, theme);
                        if (!(bool)type.GetMethod("SaveGeneral", flags)!.Invoke(window, null)!) throw new Exception("SaveGeneral failed");
                        await Task.Delay(100);
                        var root = (FrameworkElement)window.Content;
                        if (root.RequestedTheme != (theme == AppThemeMode.Light ? ElementTheme.Light : ElementTheme.Dark))
                            throw new Exception("Theme not applied immediately");
                        if (!Descendants(root).OfType<TextBlock>().Any(t => t.Text == label)) throw new Exception("Language not applied immediately");
                        if (((Button)type.GetField("_saveButton", flags)!.GetValue(window)!).IsEnabled) throw new Exception("Save remains enabled");
                    }
                    if (SettingsStore.Errors.Count != 0) throw new AggregateException(SettingsStore.Errors);
                    if (Settings.Saves != 3) throw new Exception("Unexpected save count");
                    success = true;
                }
                catch (Exception error) { SettingsStore.Log(error); }
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ui-smoke.json"), JsonSerializer.Serialize(new {
                    success, saves = Settings.Saves, errors = SettingsStore.Errors.Select(e => e.ToString()).ToArray(),
                    note = "Three real WinUI SaveGeneral passes; memory-only settings and fake autostart; no tray or scenario activation."
                }, new JsonSerializerOptions { WriteIndented = true }));
                window.Close(); Current.Exit();
            });
        }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            yield return parent;
            for (int i=0; i<VisualTreeHelper.GetChildrenCount(parent); i++)
                foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
        }
    }
}
namespace RoomSwitcherTray.Core.Services
{
    public sealed class SettingsStore
    {
        public static List<Exception> Errors { get; } = [];
        public AppSettings Current { get; } = new();
        public bool IsConfigured => false;
        public int Saves { get; private set; }
        public event EventHandler? Saved;
        public void Load() { }
        public void Save() { Saves++; Saved?.Invoke(this, EventArgs.Empty); }
        public static void Log(Exception error) => Errors.Add(error);
    }
    public static class StartupService
    {
        public static bool IsEnabled() => false;
        public static void SetEnabled(bool enabled) { }
    }
}
