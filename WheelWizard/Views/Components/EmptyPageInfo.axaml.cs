using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public class EmptyPageInfo : TemplatedControl
{
    public static readonly StyledProperty<Geometry> IconDataProperty = AvaloniaProperty.Register<EmptyPageInfo, Geometry>(nameof(IconData));

    public Geometry IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<string> BodyTextProperty = AvaloniaProperty.Register<EmptyPageInfo, string>(
        nameof(BodyText),
        "It seems like there is no content to  display here."
    );

    public string BodyText
    {
        get => GetValue(BodyTextProperty);
        set => SetValue(BodyTextProperty, value);
    }

    public static readonly StyledProperty<string> TitleProperty = AvaloniaProperty.Register<EmptyPageInfo, string>(
        nameof(Title),
        "This is empty"
    );

    public string Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
