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
        // winner badges
        { BadgeVariant.Firestarter_GoldWinner, "Firestarter Tournament Winner" },
        { BadgeVariant.Firestarter_SilverWinner, "Firestarter Tournament Runner-Up" },
        { BadgeVariant.Firestarter_BronzeWinner, "Firestarter Tournament Runner-Up" },
        { BadgeVariant.SummitShowdown_GoldWinner, "Summit Showdown Tourney Winner" },
        { BadgeVariant.SummitShowdown_SilverWinner, "Summit Showdown Tourney Runner-Up" },
        { BadgeVariant.SummitShowdown_BronzeWinner, "Summit Showdown Tourney Runner-Up" },
    };

    public static readonly StyledProperty<string> HoverTipProperty = AvaloniaProperty.Register<Badge, string>(
        nameof(HoverTip),
        BadgeToolTip[BadgeVariant.None]
    );

    public string HoverTip
    {
        get => GetValue(HoverTipProperty);
        set => SetValue(HoverTipProperty, value);
    }

    public static readonly StyledProperty<BadgeVariant> VariantProperty = AvaloniaProperty.Register<Badge, BadgeVariant>(nameof(Variant));

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
