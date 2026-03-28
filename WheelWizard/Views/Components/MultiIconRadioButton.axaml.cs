using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace WheelWizard.Views.Components;

public class MultiIconRadioButton : RadioButton
{
    #region MultiColoredIcon Colors

    public static readonly StyledProperty<IBrush?> Color1Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color1)
    );

    public IBrush? Color1
    {
        get => GetValue(Color1Property);
        set => SetValue(Color1Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color2Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color2)
    );

    public IBrush? Color2
    {
        get => GetValue(Color2Property);
        set => SetValue(Color2Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color3Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color3)
    );

    public IBrush? Color3
    {
        get => GetValue(Color3Property);
        set => SetValue(Color3Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color4Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color4)
    );

    public IBrush? Color4
    {
        get => GetValue(Color4Property);
        set => SetValue(Color4Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color5Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color5)
    );

    public IBrush? Color5
    {
        get => GetValue(Color5Property);
        set => SetValue(Color5Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color6Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color6)
    );

    public IBrush? Color6
    {
        get => GetValue(Color6Property);
        set => SetValue(Color6Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color7Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color7)
    );

    public IBrush? Color7
    {
        get => GetValue(Color7Property);
        set => SetValue(Color7Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color8Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color8)
    );

    public IBrush? Color8
    {
        get => GetValue(Color8Property);
        set => SetValue(Color8Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color9Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color9)
    );

    public IBrush? Color9
    {
        get => GetValue(Color9Property);
        set => SetValue(Color9Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color10Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color10)
    );

    public IBrush? Color10
    {
        get => GetValue(Color10Property);
        set => SetValue(Color10Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color11Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color11)
    );

    public IBrush? Color11
    {
        get => GetValue(Color11Property);
        set => SetValue(Color11Property, value);
    }

    public static readonly StyledProperty<IBrush?> Color12Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(Color12)
    );

    public IBrush? Color12
    {
        get => GetValue(Color12Property);
        set => SetValue(Color12Property, value);
    }

    #endregion

    #region MultiColoredIcon properties

    public static readonly StyledProperty<DrawingImage> IconDataProperty = AvaloniaProperty.Register<MultiIconRadioButton, DrawingImage>(
        nameof(IconData)
    );

    public DrawingImage IconData
    {
        get => GetValue(IconDataProperty);
        set => SetValue(IconDataProperty, value);
    }

    public static readonly StyledProperty<Geometry> IconGeoProperty = AvaloniaProperty.Register<MultiIconRadioButton, Geometry>(
        nameof(IconGeo)
    );
    public Geometry IconGeo
    {
        get => GetValue(IconGeoProperty);
        set => SetValue(IconGeoProperty, value);
    }

    public static readonly StyledProperty<bool> UndefinedColorsTransparentProperty = AvaloniaProperty.Register<MultiIconRadioButton, bool>(
        nameof(UndefinedColorsTransparent)
    );

    public bool UndefinedColorsTransparent
    {
        get => GetValue(UndefinedColorsTransparentProperty);
        private set => SetValue(UndefinedColorsTransparentProperty, value);
    }

    #endregion

    #region MultiIconRadioButton Hover Colors

    public static readonly StyledProperty<IBrush?> HoverColor1Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor1)
    );

    public IBrush? HoverColor1
    {
        get => GetValue(HoverColor1Property);
        set => SetValue(HoverColor1Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor2Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor2)
    );

    public IBrush? HoverColor2
    {
        get => GetValue(HoverColor2Property);
        set => SetValue(HoverColor2Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor3Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor3)
    );

    public IBrush? HoverColor3
    {
        get => GetValue(HoverColor3Property);
        set => SetValue(HoverColor3Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor4Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor4)
    );

    public IBrush? HoverColor4
    {
        get => GetValue(HoverColor4Property);
        set => SetValue(HoverColor4Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor5Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor5)
    );

    public IBrush? HoverColor5
    {
        get => GetValue(HoverColor5Property);
        set => SetValue(HoverColor5Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor6Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor6)
    );

    public IBrush? HoverColor6
    {
        get => GetValue(HoverColor6Property);
        set => SetValue(HoverColor6Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor7Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor7)
    );

    public IBrush? HoverColor7
    {
        get => GetValue(HoverColor7Property);
        set => SetValue(HoverColor7Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor8Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor8)
    );

    public IBrush? HoverColor8
    {
        get => GetValue(HoverColor8Property);
        set => SetValue(HoverColor8Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor9Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor9)
    );

    public IBrush? HoverColor9
    {
        get => GetValue(HoverColor9Property);
        set => SetValue(HoverColor9Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor10Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor10)
    );

    public IBrush? HoverColor10
    {
        get => GetValue(HoverColor10Property);
        set => SetValue(HoverColor10Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor11Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor11)
    );

    public IBrush? HoverColor11
    {
        get => GetValue(HoverColor11Property);
        set => SetValue(HoverColor11Property, value);
    }

    public static readonly StyledProperty<IBrush?> HoverColor12Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(HoverColor12)
    );

    public IBrush? HoverColor12
    {
        get => GetValue(HoverColor12Property);
        set => SetValue(HoverColor12Property, value);
    }

    #endregion

    #region MultiIconRadioButton Selected Colors

    public static readonly StyledProperty<IBrush?> SelectedColor1Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor1)
    );

    public IBrush? SelectedColor1
    {
        get => GetValue(SelectedColor1Property);
        set => SetValue(SelectedColor1Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor2Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor2)
    );

    public IBrush? SelectedColor2
    {
        get => GetValue(SelectedColor2Property);
        set => SetValue(SelectedColor2Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor3Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor3)
    );

    public IBrush? SelectedColor3
    {
        get => GetValue(SelectedColor3Property);
        set => SetValue(SelectedColor3Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor4Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor4)
    );

    public IBrush? SelectedColor4
    {
        get => GetValue(SelectedColor4Property);
        set => SetValue(SelectedColor4Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor5Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor5)
    );

    public IBrush? SelectedColor5
    {
        get => GetValue(SelectedColor5Property);
        set => SetValue(SelectedColor5Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor6Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor6)
    );

    public IBrush? SelectedColor6
    {
        get => GetValue(SelectedColor6Property);
        set => SetValue(SelectedColor6Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor7Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor7)
    );

    public IBrush? SelectedColor7
    {
        get => GetValue(SelectedColor7Property);
        set => SetValue(SelectedColor7Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor8Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor8)
    );

    public IBrush? SelectedColor8
    {
        get => GetValue(SelectedColor8Property);
        set => SetValue(SelectedColor8Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor9Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor9)
    );

    public IBrush? SelectedColor9
    {
        get => GetValue(SelectedColor9Property);
        set => SetValue(SelectedColor9Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor10Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor10)
    );

    public IBrush? SelectedColor10
    {
        get => GetValue(SelectedColor10Property);
        set => SetValue(SelectedColor10Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor11Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor11)
    );

    public IBrush? SelectedColor11
    {
        get => GetValue(SelectedColor11Property);
        set => SetValue(SelectedColor11Property, value);
    }

    public static readonly StyledProperty<IBrush?> SelectedColor12Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(SelectedColor12)
    );

    public IBrush? SelectedColor12
    {
        get => GetValue(SelectedColor12Property);
        set => SetValue(SelectedColor12Property, value);
    }

    #endregion

    #region MultiIconRadioButton Disabled Colors

    public static readonly StyledProperty<IBrush?> DisabledColor1Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor1)
    );

    public IBrush? DisabledColor1
    {
        get => GetValue(DisabledColor1Property);
        set => SetValue(DisabledColor1Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor2Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor2)
    );

    public IBrush? DisabledColor2
    {
        get => GetValue(DisabledColor2Property);
        set => SetValue(DisabledColor2Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor3Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor3)
    );

    public IBrush? DisabledColor3
    {
        get => GetValue(DisabledColor3Property);
        set => SetValue(DisabledColor3Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor4Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor4)
    );

    public IBrush? DisabledColor4
    {
        get => GetValue(DisabledColor4Property);
        set => SetValue(DisabledColor4Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor5Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor5)
    );

    public IBrush? DisabledColor5
    {
        get => GetValue(DisabledColor5Property);
        set => SetValue(DisabledColor5Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor6Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor6)
    );

    public IBrush? DisabledColor6
    {
        get => GetValue(DisabledColor6Property);
        set => SetValue(DisabledColor6Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor7Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor7)
    );

    public IBrush? DisabledColor7
    {
        get => GetValue(DisabledColor7Property);
        set => SetValue(DisabledColor7Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor8Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor8)
    );

    public IBrush? DisabledColor8
    {
        get => GetValue(DisabledColor8Property);
        set => SetValue(DisabledColor8Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor9Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor9)
    );

    public IBrush? DisabledColor9
    {
        get => GetValue(DisabledColor9Property);
        set => SetValue(DisabledColor9Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor10Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor10)
    );

    public IBrush? DisabledColor10
    {
        get => GetValue(DisabledColor10Property);
        set => SetValue(DisabledColor10Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor11Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor11)
    );

    public IBrush? DisabledColor11
    {
        get => GetValue(DisabledColor11Property);
        set => SetValue(DisabledColor11Property, value);
    }

    public static readonly StyledProperty<IBrush?> DisabledColor12Property = AvaloniaProperty.Register<MultiIconRadioButton, IBrush?>(
        nameof(DisabledColor12)
    );

    public IBrush? DisabledColor12
    {
        get => GetValue(DisabledColor12Property);
        set => SetValue(DisabledColor12Property, value);
    }

    #endregion

    public MultiIconRadioButton()
    {
        Width = 60;
        Height = 60;
    }
}
