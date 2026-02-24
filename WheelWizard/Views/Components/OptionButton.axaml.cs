using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public class OptionButton : Avalonia.Controls.Button // Change to TemplatedControl
{
    private Border? _hoverEffect;
    public static readonly StyledProperty<Geometry> IconDataProperty = AvaloniaProperty.Register<OptionButton, Geometry>(nameof(IconData));

    public static readonly StyledProperty<double> IconSizeProperty = AvaloniaProperty.Register<OptionButton, double>(
        nameof(IconSize),
        20.0
    );

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<OptionButton, string>(nameof(Text));

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public double IconSize
    {
        get => GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public OptionButton()
    {
        FontSize = 36;
        Width = 150;
        Height = 150;
        IconSize = 70;
        Margin = new(6);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_hoverEffect == null)
            return;

        var position = e.GetPosition(this);

        var left = position.X - (_hoverEffect.Width / 2);
        var top = position.Y - (_hoverEffect.Height / 2);

        _hoverEffect.Margin = new(left, top, 0, 0);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _hoverEffect = e.NameScope.Find<Border>("PART_HoverEffect");
    }
}
