using Avalonia;
using Avalonia.Media;
using Microsoft.Extensions.Caching.Memory;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.Shared.Calendar;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// The view behind <see cref="MiiImageLoader"/>. <see cref="BaseMiiImage.GeneratedImages"/> holds the image and,
/// second, either the full-quality one (<see cref="LowQualitySpeedup"/>) or the hover one (<see cref="HoverVariant"/>).
/// </summary>
public partial class MiiImageView : BaseMiiImage
{
    private bool _hasLoadedHoverVariant;

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty =
        MiiImageControl.ImageVariantProperty.AddOwner<MiiImageView>();

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<bool> LowQualitySpeedupProperty =
        MiiImageLoader.LowQualitySpeedupProperty.AddOwner<MiiImageView>();

    public bool LowQualitySpeedup
    {
        get => GetValue(LowQualitySpeedupProperty);
        set => SetValue(LowQualitySpeedupProperty, value);
    }

    public static readonly StyledProperty<Thickness> ImageOnlyMarginProperty =
        MiiImageLoader.ImageOnlyMarginProperty.AddOwner<MiiImageView>();

    public Thickness ImageOnlyMargin
    {
        get => GetValue(ImageOnlyMarginProperty);
        set => SetValue(ImageOnlyMarginProperty, value);
    }

    public static readonly StyledProperty<bool> IsHoveredProperty = MiiImageLoader.IsHoveredProperty.AddOwner<MiiImageView>();

    public bool IsHovered
    {
        get => GetValue(IsHoveredProperty);
        set => SetValue(IsHoveredProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications?> HoverVariantProperty =
        MiiImageLoader.HoverVariantProperty.AddOwner<MiiImageView>();

    public MiiImageSpecifications? HoverVariant
    {
        get => GetValue(HoverVariantProperty);
        set => SetValue(HoverVariantProperty, value);
    }

    public static readonly StyledProperty<bool> ShowSecondImageProperty = AvaloniaProperty.Register<MiiImageView, bool>(
        nameof(ShowSecondImage)
    );

    /// <summary>Whether the second image (full quality, or the hover one while hovered) is the one shown.</summary>
    public bool ShowSecondImage
    {
        get => GetValue(ShowSecondImageProperty);
        private set => SetValue(ShowSecondImageProperty, value);
    }

    public MiiImageView(IMiiImagesSingletonService images, ISeasonalCalendar calendar)
        : base(images)
    {
        InitializeComponent();

        if (calendar.IsAprilFirst)
            MiiImageContainer.RenderTransform = new RotateTransform(Random.Shared.NextDouble() * 360);

        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GeneratedImages))
                UpdateImageVisibility();
        };
        GeneratedImages.CollectionChanged += (_, _) => UpdateImageVisibility();
        MiiImageLoaded += (_, _) => UpdateImageVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == ImageVariantProperty || change.Property == HoverVariantProperty)
            RefreshCurrentMii();
        else if (change.Property == IsHoveredProperty)
        {
            if (IsHovered)
                TryLoadHoverVariant();
            UpdateImageVisibility();
        }
    }

    protected override void OnMiiChanged(Mii? newMii) => RefreshCurrentMii();

    public override void RefreshCurrentMii()
    {
        _hasLoadedHoverVariant = false;
        if (HoverVariant is null && LowQualitySpeedup)
        {
            if (GeneratedImages.Count > 0)
                GeneratedImages[0] = null;
            if (GeneratedImages.Count > 1)
                GeneratedImages[1] = null;
            OnPropertyChanged(nameof(GeneratedImages));
            ReloadImages(Mii, [GetLowQualityClone(ImageVariant), ImageVariant]);
            return;
        }

        ReloadImages(Mii, [ImageVariant]);
        if (IsHovered)
            TryLoadHoverVariant();
    }

    private void TryLoadHoverVariant()
    {
        if (Mii == null || HoverVariant == null || _hasLoadedHoverVariant)
            return;

        _hasLoadedHoverVariant = true;
        ReloadImages(Mii, [ImageVariant, HoverVariant]);
    }

    private void UpdateImageVisibility() =>
        ShowSecondImage = GeneratedImages.Count > 1 && GeneratedImages[1] != null && (HoverVariant is null || IsHovered);

    private static MiiImageSpecifications GetLowQualityClone(MiiImageSpecifications specifications)
    {
        var lowQualityClone = specifications.Clone();
        lowQualityClone.Size = MiiImageSpecifications.ImageSize.small;
        lowQualityClone.CachePriority = CacheItemPriority.Low;
        return lowQualityClone;
    }
}
