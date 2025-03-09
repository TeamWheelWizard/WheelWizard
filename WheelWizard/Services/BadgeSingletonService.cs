using WheelWizard.Helpers;
using WheelWizard.Models.Enums;

namespace WheelWizard.Services;

public interface IBadgeSingletonService
{
    Task<BadgeVariant[]> GetBadgeVariantsAsync(string friendCode);
}

public class BadgeSingletonService : IBadgeSingletonService
{
    public static readonly Dictionary<BadgeVariant, string> BadgeToolTip = new()
    {
        { BadgeVariant.None, "Whoops, the devs made an oopsie!" },
        { BadgeVariant.WhWzDev, "Wheel Wizard Developer (hiii!)" },
        { BadgeVariant.RrDev, "Retro Rewind Developer" },
        { BadgeVariant.Translator, "Translator" },
        { BadgeVariant.GoldWinner, "This is an award winning player" },
        { BadgeVariant.SilverWinner, "This is an award winning player" },
        { BadgeVariant.BronzeWinner, "This is an award winning player" }
    };

    private Dictionary<string, BadgeVariant[]>? _badgeData;

    private async Task LoadBadgesAsync()
    {
        var response = await HttpClientHelper.GetAsync<Dictionary<string, string[]>>(Endpoints.WhWzBadgesUrl);
        if (response.Content == null || !response.Succeeded)
        {
            _badgeData = [];
            return;
        }

        _badgeData = response.Content.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Select(b => Enum.TryParse(b, out BadgeVariant v) ? v : BadgeVariant.None)
                .Where(b => b != BadgeVariant.None)
                .ToArray()
        );
    }

    public async Task<BadgeVariant[]> GetBadgeVariantsAsync(string friendCode)
    {
        if (_badgeData == null)
            await LoadBadgesAsync();

        return _badgeData!.TryGetValue(friendCode, out var value) ? value : [];
    }
}
