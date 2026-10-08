using Avalonia;
using Avalonia.Controls.Primitives;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData.Views;

/// <summary>
/// One step of the leaderboard podium with its player's name and VR. Its Mii stands on top of it, drawn by the
/// <see cref="LeaderboardPodiumStage"/> it's placed in. Style it with the Gold, Silver or Bronze class.
/// </summary>
public class LeaderboardPodiumStep : TemplatedControl
{
    public static readonly StyledProperty<int> RankProperty = AvaloniaProperty.Register<LeaderboardPodiumStep, int>(nameof(Rank));

    public int Rank
    {
        get => GetValue(RankProperty);
        set => SetValue(RankProperty, value);
    }

    public static readonly StyledProperty<string> PlayerNameProperty = AvaloniaProperty.Register<LeaderboardPodiumStep, string>(
        nameof(PlayerName),
        string.Empty
    );

    public string PlayerName
    {
        get => GetValue(PlayerNameProperty);
        set => SetValue(PlayerNameProperty, value);
    }

    public static readonly StyledProperty<string> VrTextProperty = AvaloniaProperty.Register<LeaderboardPodiumStep, string>(
        nameof(VrText),
        "--"
    );

    public string VrText
    {
        get => GetValue(VrTextProperty);
        set => SetValue(VrTextProperty, value);
    }

    public static readonly StyledProperty<BadgeVariant> BadgeVariantProperty = AvaloniaProperty.Register<
        LeaderboardPodiumStep,
        BadgeVariant
    >(nameof(BadgeVariant), BadgeVariant.None);

    public BadgeVariant BadgeVariant
    {
        get => GetValue(BadgeVariantProperty);
        set => SetValue(BadgeVariantProperty, value);
    }

    public static readonly StyledProperty<bool> ShowBadgeProperty = AvaloniaProperty.Register<LeaderboardPodiumStep, bool>(
        nameof(ShowBadge)
    );

    public bool ShowBadge
    {
        get => GetValue(ShowBadgeProperty);
        set => SetValue(ShowBadgeProperty, value);
    }

    public static readonly StyledProperty<bool> IsSuspiciousProperty = AvaloniaProperty.Register<LeaderboardPodiumStep, bool>(
        nameof(IsSuspicious)
    );

    public bool IsSuspicious
    {
        get => GetValue(IsSuspiciousProperty);
        set => SetValue(IsSuspiciousProperty, value);
    }
}
