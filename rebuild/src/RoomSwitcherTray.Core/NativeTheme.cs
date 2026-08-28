using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core;

internal static class NativeTheme
{
    public static void Apply(nint window, AppThemeMode theme = AppThemeMode.System)
    {
        if (window == nint.Zero) return;
        int dark = theme switch
        {
            AppThemeMode.Dark => 1,
            AppThemeMode.Light => 0,
            _ => IsSystemDark() ? 1 : 0
        };
        DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
    }

    public static bool IsSystemDark()
    {
        object? value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1);
        return value is int setting && setting == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
