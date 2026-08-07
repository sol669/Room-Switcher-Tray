using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core;

internal static class NativeTheme
{
    public static void Apply(nint window)
    {
        if (window == nint.Zero) return;
        object? value = Registry.GetValue(
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            "AppsUseLightTheme", 1);
        int dark = value is int setting && setting == 0 ? 1 : 0;
        DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
