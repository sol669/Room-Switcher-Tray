using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace RoomSwitcherTray.Core;

internal static class NativeTheme
{
    private enum PreferredAppMode
    {
        Default,
        AllowDark,
        ForceDark,
        ForceLight,
        Max
    }

    public static void Apply(nint window, AppThemeMode theme = AppThemeMode.System)
    {
        if (window == nint.Zero) return;
        bool useDark = theme switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemDark()
        };
        try
        {
            int dark = useDark ? 1 : 0;
            int result = DwmSetWindowAttribute(window, 20, ref dark, sizeof(int));
            if (result != 0) DwmSetWindowAttribute(window, 19, ref dark, sizeof(int));

            SetPreferredAppMode(useDark ? PreferredAppMode.ForceDark : PreferredAppMode.ForceLight);
            SetWindowTheme(window, useDark ? "DarkMode_Explorer" : "Explorer", null);
            FlushMenuThemes();
        }
        catch
        {
            // Undocumented theme entry points differ between Windows builds.
            // Native controls fall back to the current Windows theme.
        }
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

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode appMode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(nint window, string? subAppName, string? subIdList);
}
