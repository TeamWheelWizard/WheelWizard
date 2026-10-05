using Avalonia;
using Avalonia.Media;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;

namespace WheelWizard.MiiImages.Views;

public sealed class Mii3DRender : MiiImageControl
{
    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        Mii3DRender,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<bool> InteractiveProperty = AvaloniaProperty.Register<Mii3DRender, bool>(
        nameof(Interactive),
        true
    );

    public bool Interactive
    {
        get => GetValue(InteractiveProperty);
        set => SetValue(InteractiveProperty, value);
    }
}
