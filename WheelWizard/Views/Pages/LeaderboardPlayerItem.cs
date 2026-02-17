using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Pages;

public sealed class LeaderboardPlayerItem
{
    public required int Rank { get; init; }
    public required string PlacementLabel { get; init; }
    public required string Name { get; init; }
    public required string FriendCode { get; init; }
    public required string VrText { get; init; }
    public Mii? Mii { get; init; }
    public BadgeVariant PrimaryBadge { get; init; }
    public bool HasBadge { get; init; }
    public bool IsSuspicious { get; init; }
    public bool IsEvenRow { get; init; }

    // Keep parity with RoomDetailsPage player template bindings.
    public string VrDisplay => VrText;
    public Mii? FirstMii => Mii;
    public bool HasBadges => HasBadge;
    public bool IsTopLeaderboardPlayer => true;
    public string TopLabel => $"#{Rank}";
    public bool IsOpenHost => false;
}
