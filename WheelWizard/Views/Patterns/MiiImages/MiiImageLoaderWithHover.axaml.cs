using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Patterns;

public partial class MiiImageLoaderWithHover : BaseMiiImage
{
    private bool _hasLoadedHoverVariant;

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
        loader._hasLoadedHoverVariant = false;
        if (loader.Mii != null)
            loader.ReloadPrimaryVariant();
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
            if (IsHovered)
                TryLoadHoverVariant();
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
        _hasLoadedHoverVariant = false;
        ReloadPrimaryVariant();
        if (IsHovered)
            TryLoadHoverVariant();
    }

    protected override void OnMiiChanged(Mii? newMii)
    {
        _hasLoadedHoverVariant = false;
        ReloadPrimaryVariant();
        if (IsHovered)
            TryLoadHoverVariant();
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

        _hasLoadedHoverVariant = HoverVariant != null;
        ReloadImages(Mii, variants);
    }

    private void ReloadPrimaryVariant()
    {
        ReloadImages(Mii, [ImageVariant]);
    }

    private void TryLoadHoverVariant()
    {
        if (Mii == null || HoverVariant == null || _hasLoadedHoverVariant)
            return;

        ReloadBothVariants();
    }
}
