using System.Drawing;
using System.Drawing.Drawing2D;

namespace RoomSwitcherTray.Core.Services;

/// <summary>Shared 32-unit artwork for WinUI previews and the notification icon.</summary>
internal static class ScenarioArtwork
{
    public const float StrokeWidth = 2.6f;

    public static GraphicsPath CreatePath(ScenarioIcon icon, string? letters)
    {
        if (icon == ScenarioIcon.Letters) return LetterPath(letters);
        var path = new GraphicsPath();
        // Shapes restored from the original TrayIconFactory (622f92d).
        switch (icon)
        {
            case ScenarioIcon.Television:
                RoundedRectangle(path, new RectangleF(3, 6, 26, 17), 3);
                Line(path, 8, 23, 6, 27);
                Line(path, 24, 23, 26, 27);
                Arc(path, -7, 12, 20, 20, 270, 90);
                break;
            case ScenarioIcon.Sofa:
                RoundedRectangle(path, new RectangleF(5, 11, 22, 13), 4);
                Line(path, 16, 11, 16, 24);
                Line(path, 3, 17, 3, 27);
                Line(path, 29, 17, 29, 27);
                Line(path, 3, 24, 29, 24);
                break;
            case ScenarioIcon.Gamepad:
                path.StartFigure();
                path.AddBezier(7, 11, 1, 18, 2, 28, 9, 23);
                path.AddBezier(9, 23, 13, 20, 19, 20, 23, 23);
                path.AddBezier(23, 23, 30, 28, 31, 18, 25, 11);
                path.CloseFigure();
                Line(path, 9, 14, 9, 20);
                Line(path, 6, 17, 12, 17);
                path.AddEllipse(21, 15, 2, 2);
                path.AddEllipse(24, 18, 2, 2);
                break;
            default:
                RoundedRectangle(path, new RectangleF(5, 4, 22, 18), 3);
                Line(path, 14.4f, 22, 14.4f, 26);
                Line(path, 17.6f, 22, 17.6f, 26);
                Line(path, 10, 26, 22, 26);
                Arc(path, -4.9f, 10.3f, 19.8f, 18.9f, 270, 90);
                break;
        }
        RectangleF bounds = path.GetBounds();
        float scale = Math.Min(28 / bounds.Width, 26 / bounds.Height);
        using var transform = new Matrix(scale, 0, 0, scale,
            (32 - bounds.Width * scale) / 2 - bounds.X * scale,
            (32 - bounds.Height * scale) / 2 - bounds.Y * scale);
        path.Transform(transform);
        return path;
    }

    private static GraphicsPath LetterPath(string? letters)
    {
        string text = ScenarioDefinition.MakeIconLetters(letters);
        if (text.Length == 0) text = "AB";
        var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        using var format = new StringFormat(StringFormat.GenericTypographic);
        path.AddString(text, family, (int)FontStyle.Bold, 32, PointF.Empty, format);
        RectangleF bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.Reset();
            path.AddString("AB", family, (int)FontStyle.Bold, 32, PointF.Empty, format);
            bounds = path.GetBounds();
        }
        // Fit actual glyph outlines, not the font's ascent/descent padding. Two
        // letters share the width but keep the same cap height as a single letter.
        float scaleY = 26 / bounds.Height;
        float scaleX = Math.Min(scaleY, 28 / bounds.Width);
        using var transform = new Matrix(scaleX, 0, 0, scaleY,
            (32 - bounds.Width * scaleX) / 2 - bounds.X * scaleX,
            (32 - bounds.Height * scaleY) / 2 - bounds.Y * scaleY);
        path.Transform(transform);
        return path;
    }

    private static void Line(GraphicsPath path, float x1, float y1, float x2, float y2)
    {
        path.StartFigure();
        path.AddLine(x1, y1, x2, y2);
    }

    private static void Arc(GraphicsPath path, float x, float y, float width, float height, float start, float sweep)
    {
        path.StartFigure();
        path.AddArc(x, y, width, height, start, sweep);
    }

    private static void RoundedRectangle(GraphicsPath path, RectangleF rect, float radius)
    {
        float diameter = radius * 2;
        path.StartFigure();
        path.AddArc(rect.Left, rect.Top, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Top, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
    }
}
