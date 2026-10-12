using Avalonia;
using Avalonia.Controls.Primitives;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData.Views;

public class CommunityBadge : TemplatedControl
{
    private static readonly Dictionary<BadgeVariant, string> BadgeToolTip = new()
    {
        { BadgeVariant.None, "This is not a badge" },
        { BadgeVariant.WhWzDev, "Wheel Wizard Developer (hiii!)" },
        { BadgeVariant.RrDev, "Retro Rewind Developer" },
        { BadgeVariant.Translator, "Translator" },
        { BadgeVariant.TranslatorLead, "Translator Lead" },
        { BadgeVariant.Heart, "Heart of the Community" },
        // winner badges
        { BadgeVariant.Firestarter_GoldWinner, "Firestarter Tournament Winner" },
        { BadgeVariant.Firestarter_SilverWinner, "Firestarter Tournament Runner-Up" },
        { BadgeVariant.Firestarter_BronzeWinner, "Firestarter Tournament Runner-Up" },
        { BadgeVariant.SummitShowdown_GoldWinner, "Summit Showdown Tournament Winner" },
        { BadgeVariant.SummitShowdown_SilverWinner, "Summit Showdown Tournament Runner-Up" },
        { BadgeVariant.SummitShowdown_BronzeWinner, "Summit Showdown Tournament Runner-Up" },
        { BadgeVariant.Leafstruck_GoldWinner, "Leafstruck Tournament Winner" },
        { BadgeVariant.Leafstruck_SilverWinner, "Leafstruck Tournament Runner-Up" },
        { BadgeVariant.Leafstruck_BronzeWinner, "Leafstruck Tournament Runner-Up" },
    };

    public static readonly StyledProperty<string> HoverTipProperty = AvaloniaProperty.Register<CommunityBadge, string>(
        nameof(HoverTip),
        BadgeToolTip[BadgeVariant.None]
    );

    public string HoverTip
    {
        get => GetValue(HoverTipProperty);
        set => SetValue(HoverTipProperty, value);
    }

    public static readonly StyledProperty<BadgeVariant> VariantProperty = AvaloniaProperty.Register<CommunityBadge, BadgeVariant>(
        nameof(Variant)
    );

    public BadgeVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private void UpdateTooltip(BadgeVariant variant)
    {
        HoverTip = BadgeToolTip.GetValueOrDefault(variant, BadgeToolTip[BadgeVariant.None]);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
            UpdateTooltip(change.GetNewValue<BadgeVariant>());
    }
}
