using Avalonia;
using Avalonia.Media;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;

namespace WheelWizard.Views.Patterns;

public sealed class MiiImageLoader : MiiImageControl
{
    public static readonly StyledProperty<bool> LowQualitySpeedupProperty = AvaloniaProperty.Register<MiiImageLoader, bool>(
        nameof(LowQualitySpeedup)
    );

    public bool LowQualitySpeedup
    {
        get => GetValue(LowQualitySpeedupProperty);
        set => SetValue(LowQualitySpeedupProperty, value);
    }

    public static readonly StyledProperty<IBrush> LoadingColorProperty = AvaloniaProperty.Register<MiiImageLoader, IBrush>(
        nameof(LoadingColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral900)
    );

    public IBrush LoadingColor
    {
        get => GetValue(LoadingColorProperty);
        set => SetValue(LoadingColorProperty, value);
    }

    public static readonly StyledProperty<IBrush> FallBackColorProperty = AvaloniaProperty.Register<MiiImageLoader, IBrush>(
        nameof(FallBackColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral700)
    );

    public IBrush FallBackColor
    {
        get => GetValue(FallBackColorProperty);
        set => SetValue(FallBackColorProperty, value);
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

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiImageLoader,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }
}
