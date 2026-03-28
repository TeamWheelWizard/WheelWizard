using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;

namespace WheelWizard.Views.Components;

public partial class PopupListButton : Button
{
    private Border? _hoverEffect;

    // The Type is not used by itself, but will probably be used a lot when using this button
    public static readonly StyledProperty<Type?> TypeProperty = AvaloniaProperty.Register<PopupListButton, Type?>(nameof(Type));
    public Type? Type
    {
        get => GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
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
