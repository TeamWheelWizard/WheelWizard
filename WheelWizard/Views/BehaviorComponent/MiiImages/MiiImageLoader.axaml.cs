using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Caching.Memory;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.BehaviorComponent;

public partial class MiiImageLoader : BaseMiiImage
{
    #region properties

    public static readonly StyledProperty<bool> LowQualitySpeedupProperty = AvaloniaProperty.Register<MiiCarousel, bool>(
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
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall, coerce: CoerceVariant);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    private static MiiImageSpecifications CoerceVariant(AvaloniaObject o, MiiImageSpecifications value)
    {
        ((MiiImageLoader)o).OnVariantChanged(value);
        return value;
    }

    #endregion

    public MiiImageLoader()
    {
        InitializeComponent();
    }

    protected void OnVariantChanged(MiiImageSpecifications newSpecifications)
    {
        List<MiiImageSpecifications> variants = [];

        if (LowQualitySpeedup)
        {
            if (GeneratedImages.Count > 1)
                GeneratedImages[1] = null;
            variants.Add(GetLowQualityClone(newSpecifications));
        }

        variants.Add(newSpecifications);
        ReloadImages(Mii, variants);
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        List<MiiImageSpecifications> variants = [];

        if (LowQualitySpeedup)
        {
            if (GeneratedImages.Count > 1)
                GeneratedImages[1] = null;
            variants.Add(GetLowQualityClone(ImageVariant));
        }

        variants.Add(ImageVariant);
        ReloadImages(newMii, variants);
    }

    private MiiImageSpecifications GetLowQualityClone(MiiImageSpecifications specifications)
    {
        var lowQualityClone = specifications.Clone();
        lowQualityClone.Size = MiiImageSpecifications.ImageSize.small;
        lowQualityClone.CachePriority = CacheItemPriority.Low;
        return lowQualityClone;
    }
}
