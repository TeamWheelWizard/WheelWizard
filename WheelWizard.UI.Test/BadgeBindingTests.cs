using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using WheelWizard.RrRooms.Views;
using WheelWizard.Views.Components;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WheelWizardData.Views;

namespace WheelWizard.UI.Test;

public class BadgeBindingTests
{
    [AvaloniaFact]
    public void Cards_DisplayAndRefreshSuppliedBadgesWithoutServiceLookup()
    {
        BadgeVariant[] initial = [BadgeVariant.WhWzDev, BadgeVariant.Translator];
        var card = new PlayerListItem { BadgeVariants = initial, HasBadges = true };
        var window = new Window
        {
            Content = card,
            Width = 500,
            Height = 200,
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.Equal(initial, card.GetVisualDescendants().OfType<CommunityBadge>().Select(badge => badge.Variant));

            BadgeVariant[] replacement = [BadgeVariant.RrDev];
            card.BadgeVariants = replacement;
            window.UpdateLayout();
            Assert.Equal(replacement, card.GetVisualDescendants().OfType<CommunityBadge>().Select(badge => badge.Variant));
        }
        finally
        {
            window.Close();
        }
    }
}
