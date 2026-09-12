using System.ComponentModel;
using Avalonia;
using Avalonia.Media;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.Shared.Calendar;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Patterns;

public partial class MiiHoverView : BaseMiiImage
{
    private readonly ISeasonalCalendar Calendar;
    private bool _hasLoadedHoverVariant;

    public static readonly StyledProperty<bool> IsHoveredProperty = AvaloniaProperty.Register<MiiHoverView, bool>(nameof(IsHovered), false);

    public bool IsHovered
    {
        get => GetValue(IsHoveredProperty);
        set => SetValue(IsHoveredProperty, value);
    }

    public static readonly StyledProperty<bool> ShowNormalImageProperty = AvaloniaProperty.Register<MiiHoverView, bool>(
        nameof(ShowNormalImage),
        true
    );

    public bool ShowNormalImage
    {
        get => GetValue(ShowNormalImageProperty);
        private set => SetValue(ShowNormalImageProperty, value);
    }

    public static readonly StyledProperty<bool> ShowHoverImageProperty = AvaloniaProperty.Register<MiiHoverView, bool>(
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

    public static readonly StyledProperty<IBrush> LoadingColorProperty = AvaloniaProperty.Register<MiiHoverView, IBrush>(
        nameof(LoadingColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral900)
    );

    public IBrush LoadingColor
    {
        get => GetValue(LoadingColorProperty);
        set => SetValue(LoadingColorProperty, value);
    }

    public static readonly StyledProperty<IBrush> FallBackColorProperty = AvaloniaProperty.Register<MiiHoverView, IBrush>(
        nameof(FallBackColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral700)
    );

    public IBrush FallBackColor
    {
        get => GetValue(FallBackColorProperty);
        set => SetValue(FallBackColorProperty, value);
    }

    public static readonly StyledProperty<Thickness> ImageOnlyMarginProperty = AvaloniaProperty.Register<MiiHoverView, Thickness>(
        nameof(ImageOnlyMargin),
        enableDataValidation: true
    );

    public Thickness ImageOnlyMargin
    {
        get => GetValue(ImageOnlyMarginProperty);
        set => SetValue(ImageOnlyMarginProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiHoverView,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall);

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public static readonly StyledProperty<MiiImageSpecifications?> HoverVariantProperty = AvaloniaProperty.Register<
        MiiHoverView,
        MiiImageSpecifications?
    >(nameof(HoverVariant));

    public MiiImageSpecifications? HoverVariant
    {
        get => GetValue(HoverVariantProperty);
        set => SetValue(HoverVariantProperty, value);
    }

    static MiiHoverView()
    {
        ImageVariantProperty.Changed.AddClassHandler<MiiHoverView>((view, _) => view.RefreshCurrentMii());
        HoverVariantProperty.Changed.AddClassHandler<MiiHoverView>((view, _) => view.RefreshCurrentMii());
    }

    public MiiHoverView(IMiiImagesSingletonService images, ISeasonalCalendar calendar)
        : base(images)
    {
        Calendar = calendar;
        InitializeComponent();

        if (Calendar.IsAprilFirst)
            MiiImageContainer.RenderTransform = new RotateTransform(Random.Shared.NextDouble() * 360);

        PropertyChanged += MiiHoverView_PropertyChanged;
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

    private void MiiHoverView_PropertyChanged(object? sender, PropertyChangedEventArgs e)
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

    public override void RefreshCurrentMii() => OnMiiChanged(Mii);

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
