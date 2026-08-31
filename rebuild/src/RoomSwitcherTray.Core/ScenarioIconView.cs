using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using RoomSwitcherTray.Core.Services;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;

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

    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private SolidColorBrush? _observedBrush;
    private long _brushToken;

    public ScenarioIconView()
    {
        Width = Height = 24;
        IsTabStop = false;
        IsHitTestVisible = false;
        Content = _image;
        RegisterPropertyChangedCallback(ForegroundProperty, (_, _) => ObserveForeground());
        Loaded += (_, _) => ObserveForeground();
        Unloaded += (_, _) => DetachBrush();
        UpdateArtwork();
    }

    private void DetachBrush()
    {
        _observedBrush?.UnregisterPropertyChangedCallback(SolidColorBrush.ColorProperty, _brushToken);
        _observedBrush = null;
    }

    private void ObserveForeground()
    {
        DetachBrush();
        if (Foreground is SolidColorBrush brush)
        {
            _observedBrush = brush;
            _brushToken = brush.RegisterPropertyChangedCallback(SolidColorBrush.ColorProperty, (_, _) => UpdateArtwork());
        }
        UpdateArtwork();
    }

    private static void OnArtworkChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((ScenarioIconView)sender).UpdateArtwork();

    private void UpdateArtwork()
    {
        Windows.UI.Color color = (Foreground as SolidColorBrush)?.Color ?? Microsoft.UI.Colors.White;
        using var bitmap = ScenarioArtwork.Render(Icon, Letters,
            System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B), 96);
        var source = new WriteableBitmap(96, 96);
        BitmapData data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, 96, 96),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            using Stream output = source.PixelBuffer.AsStream();
            byte[] row = new byte[96 * 4];
            for (int y = 0; y < 96; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                output.Write(row);
            }
        }
        finally { bitmap.UnlockBits(data); }
        source.Invalidate();
        _image.Source = source;
    }
}
