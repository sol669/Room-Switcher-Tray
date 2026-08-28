using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace RoomSwitcherTray.Core.Services;

internal static class TrayIconFactory
{
    public static nint Create(int activeScenario)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = UsesLightTaskbar() ? Color.FromArgb(24, 24, 24) : Color.White;
        using var pen = new Pen(color, 2.7f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        using var path = new GraphicsPath();
        path.AddArc(4, 5, 5, 5, 180, 90);
        path.AddArc(23, 5, 5, 5, 270, 90);
        path.AddArc(23, 20, 5, 5, 0, 90);
        path.AddArc(4, 20, 5, 5, 90, 90);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
        graphics.DrawLine(pen, 14.5f, 25, 14.5f, 29);
        graphics.DrawLine(pen, 17.5f, 25, 17.5f, 29);
        graphics.DrawLine(pen, 10, 29, 22, 29);
        graphics.DrawArc(pen, -5, 12, 20, 20, 270, 90);

        if (activeScenario is >= 1 and <= 9)
        {
            using var font = new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(color);
            string number = activeScenario.ToString();
            SizeF size = graphics.MeasureString(number, font);
            graphics.DrawString(number, font, brush, 16 - size.Width / 2, 9);
        }
        return bitmap.GetHicon();
    }

    private static bool UsesLightTaskbar()
    {
        try
        {
            object? value = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")?
                .GetValue("SystemUsesLightTheme");
            return value is int number && number != 0;
        }
        catch { return false; }
    }
}
