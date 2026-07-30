using RoomSwitcherTray.Models;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray;

internal static class NativeTheme
{
    public static void Apply(AppTheme theme, nint window)
    {
        if (window == nint.Zero) return;
        int enabled = theme switch
        {
            AppTheme.Dark => 1,
            AppTheme.Light => 0,
            _ => IsSystemDark() ? 1 : 0
        };
        DwmSetWindowAttribute(window, 20, ref enabled, sizeof(int));
    }

    private static bool IsSystemDark()
    {
        object? value = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1);
        return value is int setting && setting == 0;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute,
        ref int value, int size);
}
