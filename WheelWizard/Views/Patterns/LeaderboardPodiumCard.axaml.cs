using Avalonia;
using Avalonia.Controls.Primitives;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Patterns;

public class LeaderboardPodiumCard : TemplatedControl
{
    static LeaderboardPodiumCard()
    {
        RankProperty.Changed.AddClassHandler<LeaderboardPodiumCard>(
            (card, _) =>
            {
                card.RankLabel = t("placement.n", card.Rank);
            }
        );
    }

    public static readonly StyledProperty<int> RankProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, int>(nameof(Rank));

    public static readonly StyledProperty<string> RankLabelProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, string>(
        nameof(RankLabel)
    );

    public int Rank
    {
        get => GetValue(RankProperty);
        set => SetValue(RankProperty, value);
    }

    public string RankLabel
    {
        get => GetValue(RankLabelProperty);
        private set => SetValue(RankLabelProperty, value);
    }

    public static readonly StyledProperty<string> PlacementLabelProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, string>(
        nameof(PlacementLabel),
        string.Empty
    );

    public string PlacementLabel
    {
        get => GetValue(PlacementLabelProperty);
        set => SetValue(PlacementLabelProperty, value);
    }

    public static readonly StyledProperty<string> PlayerNameProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, string>(
        nameof(PlayerName),
        string.Empty
    );

    public string PlayerName
    {
        get => GetValue(PlayerNameProperty);
        set => SetValue(PlayerNameProperty, value);
    }

    public static readonly StyledProperty<string> VrTextProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, string>(
        nameof(VrText),
        "--"
    );

    public string VrText
    {
        get => GetValue(VrTextProperty);
        set => SetValue(VrTextProperty, value);
    }

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    public static readonly StyledProperty<double> AvatarSizeProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, double>(
        nameof(AvatarSize),
        104
    );

    public double AvatarSize
    {
        get => GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    public static readonly StyledProperty<BadgeVariant> BadgeVariantProperty = AvaloniaProperty.Register<
        LeaderboardPodiumCard,
        BadgeVariant
    >(nameof(BadgeVariant), BadgeVariant.None);

    public BadgeVariant BadgeVariant
    {
        get => GetValue(BadgeVariantProperty);
        set => SetValue(BadgeVariantProperty, value);
    }

    public static readonly StyledProperty<bool> ShowBadgeProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, bool>(
        nameof(ShowBadge)
    );

    public bool ShowBadge
    {
        get => GetValue(ShowBadgeProperty);
        set => SetValue(ShowBadgeProperty, value);
    }

    public static readonly StyledProperty<bool> IsSuspiciousProperty = AvaloniaProperty.Register<LeaderboardPodiumCard, bool>(
        nameof(IsSuspicious)
    );

    public bool IsSuspicious
    {
        get => GetValue(IsSuspiciousProperty);
        set => SetValue(IsSuspiciousProperty, value);
    }
}
