using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData.Views;

public class CommunityBadge : TemplatedControl
{
    private static readonly Dictionary<BadgeVariant, string> BadgeToolTip = new()
    {
        { BadgeVariant.None, "This is not a badge" },
        { BadgeVariant.RetroRewindDeveloper, "Retro Rewind Developer" },
        { BadgeVariant.WheelWizardDeveloper, "Wheel Wizard Developer (hiii!)" },
        { BadgeVariant.MajorContributor, "Major Contributor" },
        { BadgeVariant.RWFCModerator, "Retro WFC Moderator" },
        { BadgeVariant.DiscordStaff, "Discord Staff" },
        { BadgeVariant.Contributor, "Contributor" },
        { BadgeVariant.Translator, "Translator" },
        { BadgeVariant.Supporter, "Supporter" },
        { BadgeVariant.BetaTester, "Beta Tester" },
        { BadgeVariant.Heart, "Heart of the Community" },
        // winner badges
        { BadgeVariant.FireStarterGold, "Firestarter Tournament Winner" },
        { BadgeVariant.FireStarterSilver, "Firestarter Tournament 2nd Place" },
        { BadgeVariant.FireStarterBronze, "Firestarter Tournament 3rd Place" },
        { BadgeVariant.LeafStruckGold, "Leafstruck Tournament Winner" },
        { BadgeVariant.LeafStruckSilver, "Leafstruck Tournament 2nd Place" },
        { BadgeVariant.LeafStruckBronze, "Leafstruck Tournament 3rd Place" },
        { BadgeVariant.SummitShowdownGold, "Summit Showdown Tournament Winner" },
        { BadgeVariant.SummitShowdownSilver, "Summit Showdown Tournament 2nd Place" },
        { BadgeVariant.SummitShowdownBronze, "Summit Showdown Tournament 3rd Place" },
        { BadgeVariant.HorizonGold, "Horizon Tournament Winner" },
        { BadgeVariant.HorizonSilver, "Horizon Tournament 2nd Place" },
        { BadgeVariant.HorizonBronze, "Horizon Tournament 3rd Place" },
        { BadgeVariant.SunblossomGold, "Sunblossom Tournament Winner" },
        { BadgeVariant.SunblossomSilver, "Sunblossom Tournament 2nd Place" },
        { BadgeVariant.SunblossomBronze, "Sunblossom Tournament 3rd Place" },
        { BadgeVariant.EarthboundGold, "Earthbound Tournament Winner" },
        { BadgeVariant.EarthboundSilver, "Earthbound Tournament 2nd Place" },
        { BadgeVariant.EarthboundBronze, "Earthbound Tournament 3rd Place" },
        { BadgeVariant.BotBGold, "Bottom of the Barrel Tournament Winner" },
        { BadgeVariant.BotBSilver, "Bottom of the Barrel Tournament 2nd Place" },
        { BadgeVariant.BotBBronze, "Bottom of the Barrel Tournament 3rd Place" },
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

    // None has to be the explicit default, since 0 is a real badge id.
    public static readonly StyledProperty<BadgeVariant> VariantProperty = AvaloniaProperty.Register<CommunityBadge, BadgeVariant>(
        nameof(Variant),
        BadgeVariant.None
    );

    public BadgeVariant Variant
    {
        get => GetValue(VariantProperty);
        set => SetValue(VariantProperty, value);
    }

    private void UpdateVariant(BadgeVariant variant)
    {
        HoverTip = BadgeToolTip.GetValueOrDefault(variant, BadgeToolTip[BadgeVariant.None]);

        // Tournament ids come in gold/silver/bronze triplets starting at 2000, so they all share one style per tier.
        var tournamentTier = variant >= BadgeVariant.FireStarterGold ? ((int)variant - (int)BadgeVariant.FireStarterGold) % 3 : -1;
        PseudoClasses.Set(":gold", tournamentTier == 0);
        PseudoClasses.Set(":silver", tournamentTier == 1);
        PseudoClasses.Set(":bronze", tournamentTier == 2);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == VariantProperty)
            UpdateVariant(change.GetNewValue<BadgeVariant>());
    }
}
