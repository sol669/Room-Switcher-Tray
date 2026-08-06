using Microsoft.Win32;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace RoomSwitcherTray.Services;

internal static class TrayIconFactory
{
    public static nint Create(string key)
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color color = UsesLightTaskbar() ? Color.FromArgb(24, 24, 24) : Color.White;
        using var pen = new Pen(color, 2.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };

        switch (key)
        {
            case "television": DrawTelevision(graphics, pen); break;
            case "laptop": DrawLaptop(graphics, pen); break;
            case "dual-monitors": DrawDual(graphics, pen, false); break;
            case "projector": DrawProjector(graphics, pen); break;
            case "workstation": DrawWorkstation(graphics, pen); break;
            case "gamepad": DrawGamepad(graphics, pen); break;
            case "headphones": DrawHeadphones(graphics, pen); break;
            case "speakers": DrawSpeakers(graphics, pen); break;
            case "sofa": DrawSofa(graphics, pen); break;
            case "scenario-1": DrawNumberMonitor(graphics, pen, "1"); break;
            case "scenario-2": DrawNumberMonitor(graphics, pen, "2"); break;
            case "pc-only": DrawProjection(graphics, pen, 0); break;
            case "duplicate": DrawProjection(graphics, pen, 1); break;
            case "extend": DrawProjection(graphics, pen, 2); break;
            case "second-only": DrawProjection(graphics, pen, 3); break;
            default: DrawMonitor(graphics, pen, new RectangleF(5, 4, 22, 18), true); break;
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

    private static void DrawMonitor(Graphics g, Pen p, RectangleF r, bool arc)
    {
        g.DrawRoundedRectangle(p, r, 3);
        float middle = r.Left + r.Width / 2;
        g.DrawLine(p, middle - 1.6f, r.Bottom, middle - 1.6f, r.Bottom + 4);
        g.DrawLine(p, middle + 1.6f, r.Bottom, middle + 1.6f, r.Bottom + 4);
        g.DrawLine(p, middle - 6, r.Bottom + 4, middle + 6, r.Bottom + 4);
        if (arc)
            g.DrawArc(p, r.Left - r.Width * .45f, r.Top + r.Height * .35f,
                r.Width * .9f, r.Height * 1.05f, 270, 90);
    }

    private static void DrawTelevision(Graphics g, Pen p)
    {
        var r = new RectangleF(3, 6, 26, 17);
        g.DrawRoundedRectangle(p, r, 3);
        g.DrawLine(p, 8, 23, 6, 27); g.DrawLine(p, 24, 23, 26, 27);
        g.DrawArc(p, -7, 12, 20, 20, 270, 90);
    }

    private static void DrawLaptop(Graphics g, Pen p)
    {
        var r = new RectangleF(7, 3, 18, 17);
        g.DrawRoundedRectangle(p, r, 2.5f);
        g.DrawArc(p, -2, 9, 18, 18, 270, 90);
        g.DrawLines(p, [new PointF(7, 20), new PointF(3, 27), new PointF(29, 27), new PointF(25, 20)]);
    }

    private static void DrawDual(Graphics g, Pen p, bool overlap)
    {
        RectangleF a = overlap ? new(4, 9, 16, 13) : new(2, 6, 13, 13);
        RectangleF b = overlap ? new(12, 4, 16, 13) : new(17, 6, 13, 13);
        DrawMonitor(g, p, b, true); DrawMonitor(g, p, a, true);
    }

    private static void DrawProjector(Graphics g, Pen p)
    {
        g.DrawRoundedRectangle(p, new RectangleF(3, 9, 26, 15), 4);
        g.DrawEllipse(p, 18, 12, 8, 8);
        g.DrawLine(p, 7, 13, 13, 13); g.DrawLine(p, 7, 17, 12, 17);
    }

    private static void DrawWorkstation(Graphics g, Pen p)
    {
        DrawMonitor(g, p, new RectangleF(3, 7, 18, 15), true);
        g.DrawRoundedRectangle(p, new RectangleF(20, 5, 9, 22), 2);
        g.DrawLine(p, 23, 9, 27, 9);
        g.DrawEllipse(p, 24, 22, 2, 2);
    }

    private static void DrawGamepad(Graphics g, Pen p)
    {
        g.DrawBezier(p, new PointF(7, 11), new PointF(1, 18), new PointF(2, 28), new PointF(9, 23));
        g.DrawBezier(p, new PointF(9, 23), new PointF(13, 20), new PointF(19, 20), new PointF(23, 23));
        g.DrawBezier(p, new PointF(23, 23), new PointF(30, 28), new PointF(31, 18), new PointF(25, 11));
        g.DrawLine(p, 7, 11, 25, 11); g.DrawLine(p, 9, 14, 9, 20); g.DrawLine(p, 6, 17, 12, 17);
        g.DrawEllipse(p, 21, 15, 2, 2); g.DrawEllipse(p, 24, 18, 2, 2);
    }

    private static void DrawHeadphones(Graphics g, Pen p)
    {
        g.DrawArc(p, 6, 3, 20, 23, 180, 180);
        g.DrawRoundedRectangle(p, new RectangleF(4, 15, 6, 12), 3);
        g.DrawRoundedRectangle(p, new RectangleF(22, 15, 6, 12), 3);
    }

    private static void DrawSpeakers(Graphics g, Pen p)
    {
        g.DrawRoundedRectangle(p, new RectangleF(4, 5, 10, 23), 2);
        g.DrawRoundedRectangle(p, new RectangleF(18, 5, 10, 23), 2);
        g.DrawEllipse(p, 7, 16, 4, 4); g.DrawEllipse(p, 21, 16, 4, 4);
    }

    private static void DrawSofa(Graphics g, Pen p)
    {
        g.DrawRoundedRectangle(p, new RectangleF(5, 11, 22, 13), 4);
        g.DrawLine(p, 16, 11, 16, 24); g.DrawLine(p, 3, 17, 3, 27); g.DrawLine(p, 29, 17, 29, 27);
        g.DrawLine(p, 3, 24, 29, 24);
    }

    private static void DrawNumberMonitor(Graphics g, Pen p, string number)
    {
        DrawMonitor(g, p, new RectangleF(5, 4, 22, 18), false);
        using var font = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(p.Color);
        SizeF size = g.MeasureString(number, font);
        g.DrawString(number, font, brush, 16 - size.Width / 2, 7);
    }

    private static void DrawProjection(Graphics g, Pen p, int mode)
    {
        Color active = p.Color;
        Color inactive = Color.FromArgb(110, active);
        using var muted = new Pen(inactive, p.Width)
        {
            StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round
        };
        Pen front = mode == 3 ? muted : p;
        Pen rear = mode == 0 ? muted : p;
        DrawMonitor(g, rear, new RectangleF(13, 4, 15, 13), mode is 1 or 2 or 3);
        DrawMonitor(g, front, new RectangleF(4, 10, 17, 14), mode is 0 or 1 or 2);
    }
}

internal static class DrawingExtensions
{
    public static void DrawRoundedRectangle(this Graphics graphics, Pen pen,
        RectangleF rectangle, float radius)
    {
        float diameter = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        graphics.DrawPath(pen, path);
    }
}

