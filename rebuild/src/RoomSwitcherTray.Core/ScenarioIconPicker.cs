using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using RoomSwitcherTray.Core.Services;
using Windows.System;

namespace RoomSwitcherTray.Core;

/// <summary>A native rounded flyout: the approved 4×4 icon palette, then Letters.</summary>
public sealed class ScenarioIconPicker : Button
{
    private readonly bool _english;
    private readonly Flyout _palette;
    private readonly Grid _selectedContent = new();
    private readonly List<Button> _choices = [];
    public ScenarioIcon SelectedIcon { get; private set; }
    public event EventHandler? SelectionChanged;

    public ScenarioIconPicker(ScenarioIcon selected, bool english)
    {
        _english = english;
        SelectedIcon = Enum.IsDefined(selected) ? selected : ScenarioIcon.Desktop;
        Style = (Style)Application.Current.Resources["RoomHotkeyButtonStyle"];
        Width = 244;
        Padding = new Thickness(12, 0, 12, 0);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Center;

        _selectedContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        _selectedContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _selectedContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        Content = _selectedContent;

        var items = new StackPanel { Width = 216, Spacing = 8 };
        var grid = new Grid { ColumnSpacing = 8, RowSpacing = 8 };
        for (int i = 0; i < 4; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
        }
        for (int i = 0; i < ScenarioArtwork.Palette.Count; i++)
        {
            Button button = Choice(ScenarioArtwork.Palette[i], i);
            Grid.SetRow(button, i / 4);
            Grid.SetColumn(button, i % 4);
            grid.Children.Add(button);
        }
        items.Children.Add(grid);
        Button letters = Choice(ScenarioIcon.Letters, ScenarioArtwork.Palette.Count);
        letters.Width = 216;
        letters.Height = 32;
        items.Children.Add(letters);
        var presenterStyle = new Style(typeof(FlyoutPresenter))
        {
            BasedOn = (Style)Application.Current.Resources["DefaultFlyoutPresenterStyle"]
        };
        presenterStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14)));
        presenterStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        presenterStyle.Setters.Add(new Setter(FlyoutPresenter.IsDefaultShadowEnabledProperty, true));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MinWidthProperty, 244d));
        presenterStyle.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, 244d));
        _palette = new Flyout
        {
            Content = items,
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = presenterStyle
        };
        _palette.Opened += (_, _) => _choices.First(button => (ScenarioIcon)button.Tag == SelectedIcon).Focus(FocusState.Programmatic);
        Flyout = _palette;
        RefreshSelection();
    }

    private Button Choice(ScenarioIcon icon, int index)
    {
        var button = new Button
        {
            Tag = icon, Width = 48, Height = 48,
            MinWidth = 0, MinHeight = 0,
            Padding = new Thickness(0), CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        if (icon == ScenarioIcon.Letters) button.Content = Label(icon);
        else
        {
            var preview = new ScenarioIconView { Icon = icon, Width = 28, Height = 28 };
            preview.SetBinding(Control.ForegroundProperty,
                new Binding { Source = button, Path = new PropertyPath(nameof(Foreground)) });
            button.Content = preview;
        }
        AutomationProperties.SetName(button, Label(icon));
        button.Click += (_, _) =>
        {
            bool changed = SelectedIcon != icon;
            SelectedIcon = icon;
            RefreshSelection();
            _palette.Hide();
            if (changed) SelectionChanged?.Invoke(this, EventArgs.Empty);
        };
        button.KeyDown += (_, args) => MoveFocus(index, args);
        _choices.Add(button);
        return button;
    }

    private void MoveFocus(int index, KeyRoutedEventArgs args)
    {
        int next = args.Key switch
        {
            VirtualKey.Down => Math.Min(16, index + 4),
            VirtualKey.Up => index == 16 ? 12 : index >= 4 ? index - 4 : index,
            VirtualKey.Left => index < 16 && index % 4 > 0 ? index - 1 : index,
            VirtualKey.Right => index < 16 && index % 4 < 3 ? index + 1 : index,
            VirtualKey.Home => 0,
            VirtualKey.End => 16,
            _ => int.MinValue
        };
        if (next == int.MinValue) return;
        _choices[next].Focus(FocusState.Keyboard);
        args.Handled = true;
    }

    private void RefreshSelection()
    {
        foreach (Button button in _choices)
        {
            bool selected = (ScenarioIcon)button.Tag == SelectedIcon;
            button.Style = (Style)Application.Current.Resources[selected ? "RoomSelectedPaletteButtonStyle" : "RoomPaletteButtonStyle"];
            AutomationProperties.SetHelpText(button, selected ? (_english ? "Selected" : "Выбрано") : string.Empty);
        }
        _selectedContent.Children.Clear();
        FrameworkElement value;
        if (SelectedIcon == ScenarioIcon.Letters)
            value = new TextBlock { Text = Label(SelectedIcon), VerticalAlignment = VerticalAlignment.Center };
        else
        {
            var preview = new ScenarioIconView { Icon = SelectedIcon };
            preview.SetBinding(Control.ForegroundProperty,
                new Binding { Source = this, Path = new PropertyPath(nameof(Foreground)) });
            value = preview;
        }
        value.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetColumn(value, 1);
        _selectedContent.Children.Add(value);
        var chevron = new FontIcon
        {
            Glyph = "\uE70D", FontFamily = new FontFamily("Segoe Fluent Icons"), FontSize = 12,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(chevron, 2);
        _selectedContent.Children.Add(chevron);
        string description = (_english ? "Scenario icon: " : "Иконка сценария: ") + Label(SelectedIcon);
        AutomationProperties.SetName(this, description);
    }

    private string Label(ScenarioIcon icon) => (_english, icon) switch
    {
        (true, ScenarioIcon.Letters) => "Letters", (false, ScenarioIcon.Letters) => "Литеры",
        (true, ScenarioIcon.Television) => "Television", (false, ScenarioIcon.Television) => "Телевизор",
        (true, ScenarioIcon.Desktop) => "Computer", (false, ScenarioIcon.Desktop) => "Компьютер",
        (true, ScenarioIcon.Laptop) => "Laptop", (false, ScenarioIcon.Laptop) => "Ноутбук",
        (true, ScenarioIcon.DualMonitors) => "Two monitors", (false, ScenarioIcon.DualMonitors) => "Два монитора",
        (true, ScenarioIcon.LaptopAndMonitor) => "Laptop and monitor", (false, ScenarioIcon.LaptopAndMonitor) => "Ноутбук и монитор",
        (true, ScenarioIcon.TripleMonitors) => "Three monitors", (false, ScenarioIcon.TripleMonitors) => "Три монитора",
        (true, ScenarioIcon.QuadMonitors) => "Four monitors", (false, ScenarioIcon.QuadMonitors) => "Четыре монитора",
        (true, ScenarioIcon.Gamepad) => "Gamepad", (false, ScenarioIcon.Gamepad) => "Геймпад",
        (true, ScenarioIcon.Sofa) => "Sofa", (false, ScenarioIcon.Sofa) => "Диван",
        (true, ScenarioIcon.Speakers) => "Speakers", (false, ScenarioIcon.Speakers) => "Колонки",
        (true, ScenarioIcon.Headphones) => "Headphones", (false, ScenarioIcon.Headphones) => "Наушники",
        (true, ScenarioIcon.Projector) => "Projector", (false, ScenarioIcon.Projector) => "Проектор",
        (true, ScenarioIcon.Microphone) => "Microphone", (false, ScenarioIcon.Microphone) => "Микрофон",
        (true, ScenarioIcon.Webcam) => "Webcam", (false, ScenarioIcon.Webcam) => "Веб-камера",
        (true, ScenarioIcon.Deck) => "Handheld console", (false, ScenarioIcon.Deck) => "Портативная консоль",
        (true, ScenarioIcon.DesktopAudio) => "Computer and audio", (false, ScenarioIcon.DesktopAudio) => "Компьютер и звук",
        _ => icon.ToString()
    };
}
