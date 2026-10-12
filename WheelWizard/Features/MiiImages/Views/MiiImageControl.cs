using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using WheelWizard.MiiImages.Domain;
using WheelWizard.Views.Shell;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.MiiImages.Views;

/// <summary>
/// Public XAML input surface; the control theme owns rendering-view construction (see <see cref="MiiControlThemes"/>).
/// The rendering views share these properties through <c>AddOwner</c>, which is how the theme passes them on.
/// </summary>
public abstract class MiiImageControl : TemplatedControl
{
    private BaseMiiImage? _renderer;

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<MiiImageControl, Mii?>(nameof(Mii));
    public static readonly StyledProperty<BaseMiiImage.ReloadMethodType> ReloadMethodProperty = AvaloniaProperty.Register<
        MiiImageControl,
        BaseMiiImage.ReloadMethodType
    >(nameof(ReloadMethod));

    public static readonly StyledProperty<MiiImageSpecifications> ImageVariantProperty = AvaloniaProperty.Register<
        MiiImageControl,
        MiiImageSpecifications
    >(nameof(ImageVariant), MiiImageVariants.OnlinePlayerSmall);

    public static readonly StyledProperty<IBrush> LoadingColorProperty = AvaloniaProperty.Register<MiiImageControl, IBrush>(
        nameof(LoadingColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral900)
    );

    public static readonly StyledProperty<IBrush> FallBackColorProperty = AvaloniaProperty.Register<MiiImageControl, IBrush>(
        nameof(FallBackColor),
        new SolidColorBrush(ViewUtils.Colors.Neutral700)
    );

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    public BaseMiiImage.ReloadMethodType ReloadMethod
    {
        get => GetValue(ReloadMethodProperty);
        set => SetValue(ReloadMethodProperty, value);
    }

    public MiiImageSpecifications ImageVariant
    {
        get => GetValue(ImageVariantProperty);
        set => SetValue(ImageVariantProperty, value);
    }

    public IBrush LoadingColor
    {
        get => GetValue(LoadingColorProperty);
        set => SetValue(LoadingColorProperty, value);
    }

    public IBrush FallBackColor
    {
        get => GetValue(FallBackColorProperty);
        set => SetValue(FallBackColorProperty, value);
    }

    public event EventHandler? MiiImageLoaded;

    public void RefreshCurrentMii() => _renderer?.RefreshCurrentMii();

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        if (_renderer is not null)
            _renderer.MiiImageLoaded -= Renderer_OnImageLoaded;
        base.OnApplyTemplate(e);
        _renderer = e.NameScope.Find<BaseMiiImage>("PART_Renderer");
        if (_renderer is null)
            return;
        _renderer.MiiImageLoaded += Renderer_OnImageLoaded;
        if (_renderer.MiiLoaded)
            Renderer_OnImageLoaded(_renderer, EventArgs.Empty);
    }

    private void Renderer_OnImageLoaded(object? sender, EventArgs e) => MiiImageLoaded?.Invoke(this, e);
}
