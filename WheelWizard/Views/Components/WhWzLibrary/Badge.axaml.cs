using Avalonia;
using Avalonia.Controls.Primitives;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.Views.Components;

public class Badge : TemplatedControl
{
    public static readonly Dictionary<BadgeVariant, string> BadgeToolTip = new()
    {
        { BadgeVariant.None, "This is not a badge" },
        { BadgeVariant.WhWzDev, "Wheel Wizard Developer (hiii!)" },
        { BadgeVariant.RrDev, "Retro Rewind Developer" },
        { BadgeVariant.Translator, "Translator" },
        { BadgeVariant.TranslatorLead, "Translator Lead" },
        { BadgeVariant.GoldWinner, "This is an award winning player" },
        { BadgeVariant.SilverWinner, "This is an award winning player" },
        { BadgeVariant.BronzeWinner, "This is an award winning player" },
    };
    
    public static readonly StyledProperty<string> HoverTipProperty =
        AvaloniaProperty.Register<Badge, string>(nameof(HoverTip), BadgeToolTip[BadgeVariant.None]);
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
        HoverTip = BadgeToolTip[variant];
    }
    
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
            UpdateStyleClasses(change.GetNewValue<BadgeVariant>());
    }
}

