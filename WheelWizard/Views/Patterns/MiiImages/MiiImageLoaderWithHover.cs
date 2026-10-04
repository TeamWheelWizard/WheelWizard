using Avalonia;
using Avalonia.Media;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;

namespace WheelWizard.Views.Patterns;

public sealed class MiiImageLoaderWithHover : MiiImageControl
{
    public static readonly StyledProperty<bool> IsHoveredProperty = AvaloniaProperty.Register<MiiImageLoaderWithHover, bool>(
        nameof(IsHovered),
        false
    );

    public bool IsHovered
    {
        get => GetValue(IsHoveredProperty);
        set => SetValue(IsHoveredProperty, value);
    }

    public static readonly StyledProperty<IBrush> LoadingColorProperty = AvaloniaProperty.Register<MiiImageLoaderWithHover, IBrush>(
        nameof(LoadingColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral900)
    );

    public IBrush LoadingColor
    {
        get => GetValue(LoadingColorProperty);
        set => SetValue(LoadingColorProperty, value);
    }

    public static readonly StyledProperty<IBrush> FallBackColorProperty = AvaloniaProperty.Register<MiiImageLoaderWithHover, IBrush>(
        nameof(FallBackColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral700)
    );

    public IBrush FallBackColor
    {
        get => GetValue(FallBackColorProperty);
        set => SetValue(FallBackColorProperty, value);
    }

    public static readonly StyledProperty<Thickness> ImageOnlyMarginProperty = AvaloniaProperty.Register<
        MiiImageLoaderWithHover,
        Thickness
    >(nameof(ImageOnlyMargin), enableDataValidation: true);

    public Thickness ImageOnlyMargin
    {
        get => GetValue(ImageOnlyMarginProperty);
        set => SetValue(ImageOnlyMarginProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiImageLoaderWithHover,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications?> HoverVariantProperty = AvaloniaProperty.Register<
        MiiImageLoaderWithHover,
        MiiImageSpecifications?
    >(nameof(HoverVariant));

    public MiiImageSpecifications? HoverVariant
    {
        get => GetValue(HoverVariantProperty);
        set => SetValue(HoverVariantProperty, value);
    }
}
