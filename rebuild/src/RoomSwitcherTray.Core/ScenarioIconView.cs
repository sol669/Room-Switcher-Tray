using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using RoomSwitcherTray.Core.Services;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using Windows.Foundation;

namespace RoomSwitcherTray.Core;

public sealed class ScenarioIconView : UserControl
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(ScenarioIcon), typeof(ScenarioIconView),
        new PropertyMetadata(ScenarioIcon.Desktop, OnArtworkChanged));
    public static readonly DependencyProperty LettersProperty = DependencyProperty.Register(
        nameof(Letters), typeof(string), typeof(ScenarioIconView),
        new PropertyMetadata("AB", OnArtworkChanged));

    public ScenarioIcon Icon
    {
        get => (ScenarioIcon)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Letters
    {
        get => (string)GetValue(LettersProperty);
        set => SetValue(LettersProperty, value);
    }

    private readonly Microsoft.UI.Xaml.Shapes.Path _path = new()
    {
        StrokeThickness = ScenarioArtwork.StrokeWidth,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round
    };

    public ScenarioIconView()
    {
        Width = 24;
        Height = 24;
        IsTabStop = false;
        var canvas = new Canvas { Width = 32, Height = 32 };
        canvas.Children.Add(_path);
        Content = new Viewbox { Child = canvas, Stretch = Stretch.Uniform };
        UpdateArtwork();
    }

    private static void OnArtworkChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ScenarioIconView)sender).UpdateArtwork();

    private void UpdateArtwork()
    {
        using GraphicsPath artwork = ScenarioArtwork.CreatePath(Icon, Letters);
        _path.Data = ToGeometry(artwork);
        _path.ClearValue(Microsoft.UI.Xaml.Shapes.Shape.FillProperty);
        _path.ClearValue(Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty);
        _path.SetBinding(Icon == ScenarioIcon.Letters
            ? Microsoft.UI.Xaml.Shapes.Shape.FillProperty : Microsoft.UI.Xaml.Shapes.Shape.StrokeProperty,
            new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
    }

    private static PathGeometry ToGeometry(GraphicsPath artwork)
    {
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        System.Drawing.PointF[] points = artwork.PathPoints;
        byte[] types = artwork.PathTypes;
        PathFigure? figure = null;
        for (int i = 0; i < points.Length; i++)
        {
            switch (types[i] & 7)
            {
                case 0:
                    figure = new PathFigure { StartPoint = new Point(points[i].X, points[i].Y) };
                    geometry.Figures.Add(figure);
                    break;
                case 1:
                    figure!.Segments.Add(new LineSegment { Point = new Point(points[i].X, points[i].Y) });
                    break;
                case 3:
                    figure!.Segments.Add(new BezierSegment
                    {
                        Point1 = new Point(points[i].X, points[i].Y),
                        Point2 = new Point(points[i + 1].X, points[i + 1].Y),
                        Point3 = new Point(points[i + 2].X, points[i + 2].Y)
                    });
                    i += 2;
                    break;
            }
            if ((types[i] & 0x80) != 0 && figure is not null) figure.IsClosed = true;
        }
        return geometry;
    }
}

[Microsoft.UI.Xaml.Data.Bindable]
public sealed class ScenarioIconOption : INotifyPropertyChanged
{
    public ScenarioIcon Value { get; }
    public string Name { get; }
    private string _letters;
    public string Letters
    {
        get => _letters;
        set
        {
            if (_letters == value) return;
            _letters = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Letters)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public ScenarioIconOption(ScenarioIcon value, string name, string letters = "AB") =>
        (Value, Name, _letters) = (value, name, letters);
    public override string ToString() => Name;
}
