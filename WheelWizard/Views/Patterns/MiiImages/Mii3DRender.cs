using Avalonia;
using Avalonia.Media;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;

namespace WheelWizard.Views.Patterns;

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

    public static readonly StyledProperty<float> PreviewRenderScaleProperty = AvaloniaProperty.Register<Mii3DRender, float>(
        nameof(PreviewRenderScale),
        0.2f
    );

    public float PreviewRenderScale
    {
        get => GetValue(PreviewRenderScaleProperty);
        set => SetValue(PreviewRenderScaleProperty, value);
    }

    public static readonly StyledProperty<int> HighQualitySettleDelayMsProperty = AvaloniaProperty.Register<Mii3DRender, int>(
        nameof(HighQualitySettleDelayMs),
        90
    );

    public int HighQualitySettleDelayMs
    {
        get => GetValue(HighQualitySettleDelayMsProperty);
        set => SetValue(HighQualitySettleDelayMsProperty, value);
    }
}
