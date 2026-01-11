using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using WheelWizard.MiiImages;
using WheelWizard.MiiImages.Domain;
using WheelWizard.Views.BehaviorComponent;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Components;

public class MiiBlock : RadioButton
{
    private static ContextMenu? s_oldMenu;
    private MiiImageLoaderWithHover? _miiImageLoader;

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<MiiBlock, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    public static readonly StyledProperty<string?> MiiNameProperty = AvaloniaProperty.Register<MiiBlock, string?>(nameof(MiiName));

    public string? MiiName
    {
        get => GetValue(MiiNameProperty);
        private set => SetValue(MiiNameProperty, value);
    }

    public static readonly StyledProperty<bool> IsFavoriteProperty = AvaloniaProperty.Register<MiiBlock, bool>(nameof(IsFavorite));

    public bool IsFavorite
    {
        get => GetValue(IsFavoriteProperty);
        private set => SetValue(IsFavoriteProperty, value);
    }

    public static readonly StyledProperty<bool> IsGlobalProperty = AvaloniaProperty.Register<MiiBlock, bool>(nameof(IsGlobal));

    public bool IsGlobal
    {
        get => GetValue(IsGlobalProperty);
        private set => SetValue(IsGlobalProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        _miiImageLoader = e.NameScope.Find<MiiImageLoaderWithHover>("PART_MiiImageLoader");

        // Set hover variant immediately
        if (_miiImageLoader != null)
        {
            // Create hover variant with smile expression
            var hoverVariant = MiiImageVariants.MiiBlockProfile.Clone();
            hoverVariant.Name = "MiiBlockProfileHover";
            hoverVariant.Expression = MiiImageSpecifications.FaceExpression.smile;
            _miiImageLoader.HoverVariant = hoverVariant;

            // If Mii is already set, trigger reload
            if (Mii != null)
            {
                _miiImageLoader.ReloadBothVariants();
            }
        }
    }

    private void SetupHoverVariant()
    {
        if (_miiImageLoader != null)
        {
            // Create hover variant with smile expression
            var hoverVariant = MiiImageVariants.MiiBlockProfile.Clone();
            hoverVariant.Name = "MiiBlockProfileHover";
            hoverVariant.Expression = MiiImageSpecifications.FaceExpression.smile;
            _miiImageLoader.HoverVariant = hoverVariant;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == MiiProperty)
        {
            var mii = change.GetNewValue<Mii?>();
            MiiName = mii?.Name.ToString();
            IsFavorite = mii?.IsFavorite ?? false;
            IsGlobal = mii?.IsGlobal() ?? false;

            // Ensure hover variant is set when Mii changes
            if (_miiImageLoader != null)
            {
                SetupHoverVariant();
                // Trigger reload if Mii is set
                if (mii != null)
                {
                    _miiImageLoader.ReloadBothVariants();
                }
            }
        }

        Tag = MiiName ?? String.Empty;
        ClipToBounds = string.IsNullOrWhiteSpace(MiiName);
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_miiImageLoader != null)
        {
            _miiImageLoader.IsHovered = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_miiImageLoader != null)
        {
            _miiImageLoader.IsHovered = false;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        e.Handled = true;

        var properties = e.GetCurrentPoint(this).Properties;
        if (properties.IsLeftButtonPressed)
            IsChecked = !IsChecked;

        RaiseEvent(new RoutedEventArgs(ClickEvent));
    }
}
