using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Microsoft.Extensions.Caching.Memory;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.BehaviorComponent;

public partial class MiiCarousel : BaseMiiImage
{
    private int CarouselInstanceCount = 1;
    private int CurrentCarouselInstance = 0;

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiCarousel,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall, coerce: CoerceVariant);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public MiiCarousel()
    {
        InitializeComponent();
    }

    private static MiiImageSpecifications CoerceVariant(AvaloniaObject o, MiiImageSpecifications value)
    {
        ((MiiCarousel)o).OnVariantChanged(value);
        return value;
    }

    protected void OnVariantChanged(MiiImageSpecifications newSpecifications)
    {
        CarouselInstanceCount = newSpecifications.InstanceCount;
        List<MiiImageSpecifications> variants = [GetPreviewClone(newSpecifications), newSpecifications];
        if (GeneratedImages.Count > 1)
            GeneratedImages[1] = null;
        ReloadImages(Mii, variants);
        ApplyRotation();
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        CurrentCarouselInstance = 0;
        List<MiiImageSpecifications> variants = [GetPreviewClone(ImageVariant), ImageVariant];
        if (GeneratedImages.Count > 1)
            GeneratedImages[1] = null;
        ReloadImages(newMii, variants);
        ApplyRotation();
    }

    private MiiImageSpecifications GetPreviewClone(MiiImageSpecifications specifications)
    {
        var lowQualityClone = specifications.Clone();
        lowQualityClone.Size = MiiImageSpecifications.ImageSize.small;
        lowQualityClone.CachePriority = CacheItemPriority.Low;
        lowQualityClone.InstanceCount = 1;
        return lowQualityClone;
    }

    private void ApplyRotation()
    {
        var transGroup = new TransformGroup();
        transGroup.Children.Add(new ScaleTransform(CarouselInstanceCount, CarouselInstanceCount));
        transGroup.Children.Add(new TranslateTransform(CurrentCarouselInstance * MiiImage.Bounds.Height * CarouselInstanceCount, 0));
        MiiImage.RenderTransform = transGroup;
    }

    private void RotateLeft_Click(object? sender, RoutedEventArgs e)
    {
        CurrentCarouselInstance += 1;
        if (CurrentCarouselInstance > 0)
            CurrentCarouselInstance -= CarouselInstanceCount;
        CurrentCarouselInstance %= CarouselInstanceCount;
        ApplyRotation();
    }

    private void RotateRight_Click(object? sender, RoutedEventArgs e)
    {
        CurrentCarouselInstance -= 1;
        CurrentCarouselInstance %= CarouselInstanceCount;
        ApplyRotation();
    }

    private void ImageBorder_OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        MiiImageCounter.RenderTransform = new ScaleTransform(1.5, 1.5);
        MiiImageCounter.Margin = new(0, -ImageBorder.Bounds.Height * 0.4, 0, 0);
    }
}
