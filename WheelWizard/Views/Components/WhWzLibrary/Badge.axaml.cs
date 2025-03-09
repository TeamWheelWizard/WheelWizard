using Avalonia;
using Avalonia.Controls.Primitives;
using WheelWizard.Models.Enums;
using WheelWizard.Services;

namespace WheelWizard.Views.Components.WhWzLibrary;

public class Badge : TemplatedControl
{

    public static readonly StyledProperty<string> HoverTipProperty =
        AvaloniaProperty.Register<Badge, string>(nameof(HoverTip), 
            BadgeManager.Instance.BadgeToolTip[BadgeVariant.None]);
    public string HoverTip
    {
        get => GetValue(HoverTipProperty);
        set => SetValue(HoverTipProperty, value);
    }

    
    public static readonly StyledProperty<BadgeVariant> VariantProperty =
        AvaloniaProperty.Register<Badge, BadgeVariant>(nameof(Variant));

    public BadgeVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }
    
    private void UpdateStyleClasses(BadgeVariant variant)
    {
        var types = Enum.GetValues<BadgeVariant>();
        foreach (var enumType in types)
        {
            Classes.Remove(enumType.ToString());
        }
        Classes.Add(variant.ToString());
        HoverTip = BadgeManager.Instance.BadgeToolTip[variant];
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
            UpdateStyleClasses(change.GetNewValue<BadgeVariant>());
    }
}

