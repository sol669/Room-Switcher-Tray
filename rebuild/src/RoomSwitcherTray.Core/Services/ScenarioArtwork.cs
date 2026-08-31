using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace RoomSwitcherTray.Core.Services;

/// <summary>Approved icon exports shared by the picker, navigation and tray.</summary>
internal static class ScenarioArtwork
{
    public static readonly IReadOnlyList<ScenarioIcon> Palette = Array.AsReadOnly(new[]
    {
        ScenarioIcon.Television, ScenarioIcon.Desktop, ScenarioIcon.Laptop, ScenarioIcon.DualMonitors,
        ScenarioIcon.LaptopAndMonitor, ScenarioIcon.TripleMonitors, ScenarioIcon.QuadMonitors, ScenarioIcon.Gamepad,
        ScenarioIcon.Sofa, ScenarioIcon.Speakers, ScenarioIcon.Headphones, ScenarioIcon.Projector,
        ScenarioIcon.Microphone, ScenarioIcon.Webcam, ScenarioIcon.Deck, ScenarioIcon.DesktopAudio
    });

    public static Bitmap Render(ScenarioIcon icon, string? letters, Color color, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        try
        {
            using Graphics graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            if (icon == ScenarioIcon.Letters)
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.ScaleTransform(size / 32f, size / 32f);
                using GraphicsPath path = LetterPath(letters);
                using var brush = new SolidBrush(color);
                graphics.FillPath(brush, path);
            }
            else
            {
                if (!Palette.Contains(icon)) icon = ScenarioIcon.Desktop;
                int assetSize = new[] { 16, 20, 24, 32, 48, 64, 96 }.Contains(size) ? size : 96;
                string resource = $"RoomSwitcherTray.Core.Assets.ScenarioIcons.s{assetSize}.{icon}.png";
                using Stream stream = typeof(ScenarioArtwork).Assembly.GetManifestResourceStream(resource)
                    ?? throw new InvalidOperationException($"Missing icon resource: {resource}");
                using var source = new Bitmap(stream);
                using var attributes = new ImageAttributes();
                // Recolor the approved alpha mask, retaining its exact silhouette.
                attributes.SetColorMatrix(new ColorMatrix
                {
                    Matrix00 = 0, Matrix11 = 0, Matrix22 = 0,
                    Matrix33 = color.A / 255f, Matrix44 = 1,
                    Matrix40 = color.R / 255f, Matrix41 = color.G / 255f, Matrix42 = color.B / 255f
                });
                graphics.InterpolationMode = size == assetSize ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.Half;
                graphics.DrawImage(source, new Rectangle(0, 0, size, size),
                    0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            graphics.Flush(FlushIntention.Sync);
            return bitmap;
        }
        catch { bitmap.Dispose(); throw; }
    }

    public static Bitmap RenderWarning(Color color, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size / 32f, size / 32f);
        using var pen = new Pen(color, 2.4f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawPolygon(pen, [new PointF(16, 3), new PointF(30, 28), new PointF(2, 28)]);
        graphics.DrawLine(pen, 16, 12, 16, 19);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 14.7f, 22, 2.6f, 2.6f);
        return bitmap;
    }

    public static Bitmap RenderRemote(Color color, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
        using Graphics graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.ScaleTransform(size / 32f, size / 32f);
        using var pen = new Pen(color, 2.3f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLines(pen, [new PointF(7, 13), new PointF(15, 21), new PointF(7, 29)]);
        graphics.DrawLines(pen, [new PointF(25, 3), new PointF(17, 11), new PointF(25, 19)]);
        return bitmap;
    }

    private static GraphicsPath LetterPath(string? letters)
    {
        string text = ScenarioDefinition.MakeIconLetters(letters);
        if (text.Length == 0) text = "AB";
        var path = new GraphicsPath();
        using var family = new FontFamily("Segoe UI");
        using var format = new StringFormat(StringFormat.GenericTypographic);
        path.AddString(text, family, (int)FontStyle.Regular, 28, PointF.Empty, format);
        RectangleF bounds = path.GetBounds();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            path.Reset();
            path.AddString("AB", family, (int)FontStyle.Regular, 28, PointF.Empty, format);
            bounds = path.GetBounds();
        }
        // Ordinary Segoe UI proportions. Never stretch the two axes separately.
        float scale = Math.Min(1, Math.Min(28 / bounds.Width, 20 / bounds.Height));
        using var transform = new Matrix(scale, 0, 0, scale,
            (32 - bounds.Width * scale) / 2 - bounds.X * scale,
            (32 - bounds.Height * scale) / 2 - bounds.Y * scale);
        path.Transform(transform);
        return path;
    }
}
