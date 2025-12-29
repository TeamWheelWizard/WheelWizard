using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.BehaviorComponent;

public partial class MiiImageLoaderWithHover : BaseMiiImage
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

    public static readonly StyledProperty<bool> ShowNormalImageProperty = AvaloniaProperty.Register<MiiImageLoaderWithHover, bool>(
        nameof(ShowNormalImage),
        true
    );

    public bool ShowNormalImage
    {
        get => GetValue(ShowNormalImageProperty);
        private set => SetValue(ShowNormalImageProperty, value);
    }

    public static readonly StyledProperty<bool> ShowHoverImageProperty = AvaloniaProperty.Register<MiiImageLoaderWithHover, bool>(
        nameof(ShowHoverImage),
        false
    );

    public bool ShowHoverImage
    {
        get => GetValue(ShowHoverImageProperty);
        private set => SetValue(ShowHoverImageProperty, value);
    }

    private void UpdateImageVisibility()
    {
        var hasHoverImage = GeneratedImages.Count > 1 && GeneratedImages[1] != null;

        if (IsHovered && hasHoverImage)
        {
            ShowNormalImage = false;
            ShowHoverImage = true;
        }
        else
        {
            ShowNormalImage = true;
            ShowHoverImage = false;
        }
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
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall, coerce: CoerceVariant);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications?> HoverVariantProperty = AvaloniaProperty.Register<
        MiiImageLoaderWithHover,
        MiiImageSpecifications?
    >(nameof(HoverVariant), coerce: CoerceHoverVariant);

    public MiiImageSpecifications? HoverVariant
    {
        get => GetValue(HoverVariantProperty);
        set => SetValue(HoverVariantProperty, value);
    }

    private static MiiImageSpecifications? CoerceHoverVariant(AvaloniaObject o, MiiImageSpecifications? value)
    {
        var loader = (MiiImageLoaderWithHover)o;
        // Reload both variants when hover variant changes (if Mii is already set)
        // If Mii is not set yet, OnMiiChanged will handle the reload
        if (loader.Mii != null && value != null)
        {
            loader.ReloadBothVariants();
        }
        return value;
    }

    private static MiiImageSpecifications CoerceVariant(AvaloniaObject o, MiiImageSpecifications value)
    {
        ((MiiImageLoaderWithHover)o).OnVariantChanged(value);
        return value;
    }

    public MiiImageLoaderWithHover()
    {
        InitializeComponent();
        PropertyChanged += MiiImageLoaderWithHover_PropertyChanged;
        GeneratedImages.CollectionChanged += (s, e) => UpdateImageVisibility();
        MiiImageLoaded += (s, e) => UpdateImageVisibility();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsHoveredProperty)
        {
            UpdateImageVisibility();
        }
    }

    private void MiiImageLoaderWithHover_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GeneratedImages))
        {
            UpdateImageVisibility();
        }
    }

    private void OnVariantChanged(MiiImageSpecifications newSpecifications)
    {
        ReloadBothVariants();
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        // Always reload both variants when Mii changes
        // This ensures both images are loaded even if hover variant is set later
        ReloadBothVariants();
    }

    public void ReloadBothVariants()
    {
        if (Mii == null)
            return;

        var variants = new List<MiiImageSpecifications>();

        // Always load the normal variant first
        variants.Add(ImageVariant);

        // If hover variant is set, load it as the second image
        if (HoverVariant != null)
        {
            variants.Add(HoverVariant);
        }

        ReloadImages(Mii, variants);
    }
}
