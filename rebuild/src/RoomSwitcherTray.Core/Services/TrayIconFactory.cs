using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace RoomSwitcherTray.Core.Services;

internal static class TrayIconFactory
{
    public static nint Create(ScenarioDefinition? scenario = null)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = UsesLightTaskbar() ? Color.FromArgb(24, 24, 24) : Color.White;
        ScenarioIcon icon = scenario?.Icon ?? ScenarioIcon.Desktop;
        string? letters = string.IsNullOrWhiteSpace(scenario?.IconLetters) ? scenario?.Name : scenario.IconLetters;
        using GraphicsPath path = ScenarioArtwork.CreatePath(icon, letters);

        if (icon == ScenarioIcon.Letters)
        {
            using var brush = new SolidBrush(color);
            graphics.FillPath(brush, path);
            return bitmap.GetHicon();
        }

        using var pen = new Pen(color, ScenarioArtwork.StrokeWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        graphics.DrawPath(pen, path);

        return bitmap.GetHicon();
    }

    private static bool UsesLightTaskbar()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            object? value = key?.GetValue("SystemUsesLightTheme");
            return value is int number && number != 0;
        }
        catch { return false; }
    }
}
