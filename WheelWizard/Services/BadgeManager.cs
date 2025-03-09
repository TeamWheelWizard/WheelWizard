using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Views.Components.WhWzLibrary;

namespace WheelWizard.Services;

public class BadgeManager
{
    public readonly Dictionary<BadgeVariant, string> BadgeToolTip = new()
    {
        {BadgeVariant.None, "Whoops, the devs made an oopsie!"},
        {BadgeVariant.WhWzDev, "Wheel Wizard Developer (hiii!)"},
        {BadgeVariant.RrDev, "Retro Rewind Developer"},
        {BadgeVariant.Translator, "Translator"},
        {BadgeVariant.GoldWinner, "This is an award winning player"},
        {BadgeVariant.SilverWinner, "This is an award winning player"},
        {BadgeVariant.BronzeWinner, "This is an award winning player"}
    };
    
    public Dictionary<string,BadgeVariant[]> BadgeData { get; private set; }
    
    public static BadgeManager Instance { get; } = new();
    private BadgeManager() { }
    public async void LoadBadges()
    {
        var response = await HttpClientHelper.GetAsync<Dictionary<string,string[]>>(Endpoints.WhWzBadgesUrl);
        if (response?.Content == null || !response.Succeeded) return;
        
        BadgeData = response.Content.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value
                .Select(b => Enum.TryParse(b, out BadgeVariant v) ? v : BadgeVariant.None)
                .Where(b => b != BadgeVariant.None)
                .ToArray()
        );
    }
    
    public BadgeVariant[] GetRandomBadgeVariants(int? seed = null)
    {
        var random = seed == null ? new Random() : new Random(seed.Value);
        var allVariants = Enum.GetValues(typeof(BadgeVariant))
            .Cast<BadgeVariant>()
            .Where(variant => variant != BadgeVariant.None)
            .ToArray();
        var numberOfBadges = random.Next(0, 5); // 1 to 4 badges

        var selectedVariants = new List<BadgeVariant>();
        for (var i = 0; i < numberOfBadges; i++)
        {
            if (allVariants == null) continue;
            var randomIndex = random.Next(allVariants.Length);
            selectedVariants.Add(allVariants[randomIndex]);
        }

        return selectedVariants.ToArray();
    }
    
    public BadgeVariant[] GetBadgeVariants(string friendCode)
    {
        return BadgeData.ContainsKey(friendCode) ? BadgeData[friendCode] : [];
    }

    public IEnumerable<Badge> GetBadges(string friendCode) => GetBadges(GetBadgeVariants(friendCode));
    public IEnumerable<Badge> GetBadges(BadgeVariant[] badges) => badges.Select(variant => new Badge { Variant = variant });
}
