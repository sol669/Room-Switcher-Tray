using Microsoft.Win32;

namespace RoomSwitcherTray.Core.Services;

public static class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "RoomSwitcher";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Не удалось открыть настройки автозапуска Windows.");
        if (!enabled)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return;
        }

        string path = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к RoomSwitcher.");
        key.SetValue(ValueName, $"\"{path}\"");
    }
}
