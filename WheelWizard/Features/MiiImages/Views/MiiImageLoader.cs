using Avalonia;
using WheelWizard.MiiImages.Domain;

namespace WheelWizard.MiiImages.Views;

/// <summary>A still picture of a Mii. With a <see cref="HoverVariant"/> it shows that one instead while <see cref="IsHovered"/>.</summary>
public sealed class MiiImageLoader : MiiImageControl
{
    public static readonly StyledProperty<bool> LowQualitySpeedupProperty = AvaloniaProperty.Register<MiiImageLoader, bool>(
        nameof(LowQualitySpeedup)
    );

    /// <summary>Shows a small, quick image first while the full one loads. Not used together with a hover variant.</summary>
    public bool LowQualitySpeedup
    {
        get => GetValue(LowQualitySpeedupProperty);
        set => SetValue(LowQualitySpeedupProperty, value);
    }

    public static readonly StyledProperty<Thickness> ImageOnlyMarginProperty = AvaloniaProperty.Register<MiiImageLoader, Thickness>(
        nameof(ImageOnlyMargin),
        enableDataValidation: true
    );

    public Thickness ImageOnlyMargin
    {
        get => GetValue(ImageOnlyMarginProperty);
        set => SetValue(ImageOnlyMarginProperty, value);
    }

    public static readonly StyledProperty<bool> IsHoveredProperty = AvaloniaProperty.Register<MiiImageLoader, bool>(nameof(IsHovered));

    public bool IsHovered
    {
        get => GetValue(IsHoveredProperty);
        set => SetValue(IsHoveredProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications?> HoverVariantProperty = AvaloniaProperty.Register<
        MiiImageLoader,
        MiiImageSpecifications?
    >(nameof(HoverVariant));

    /// <summary>Picture shown while <see cref="IsHovered"/>; it's only loaded once the Mii is first hovered.</summary>
    public MiiImageSpecifications? HoverVariant
    {
        get => GetValue(HoverVariantProperty);
        set => SetValue(HoverVariantProperty, value);
    }
}
