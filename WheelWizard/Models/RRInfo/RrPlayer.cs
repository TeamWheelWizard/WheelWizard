using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Models.RRInfo;

public class RrPlayer : IEquatable<RrPlayer>
{
    public required string Pid { get; set; }
    public required string Name { get; set; }
    public required string FriendCode { get; set; }

    public int? Vr { get; set; }
    public int? Br { get; set; }

    public bool IsOpenHost { get; set; }
    public bool IsSuspended { get; set; }

    public int? LeaderboardRank { get; set; }
    public bool IsTopLeaderboardPlayer => LeaderboardRank.HasValue;
    public string TopLabel => LeaderboardRank is { } rank ? $"#{rank}" : string.Empty;

    public List<string> ConnectionMap { get; set; } = [];

    public Mii? Mii { get; set; }

    public Mii? FirstMii => Mii;

    public string VrDisplay => Vr?.ToString() ?? "--";
    public string BrDisplay => Br?.ToString() ?? "--";

    public int ConnectionAmp
    {
        get
        {
            var parsedAmps = ConnectionMap.Select(ParseConnectionAmp).Where(amp => amp > 0).ToList();

            if (parsedAmps.Count == 0)
                return 0;

            return parsedAmps
                .GroupBy(amp => amp)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Key)
                .First()
                .Key;
        }
    }

    public int ConnectionQualityBars =>
        ConnectionAmp switch
        {
            >= 3 => 3,
            >= 2 => 2,
            _ => 1,
        };

    public BadgeVariant[] BadgeVariants { get; set; } = [];
    public bool HasBadges => BadgeVariants.Length != 0;

    public bool Equals(RrPlayer? other)
    {
        if (other is null)
            return false;
        return string.Equals(Pid, other.Pid, StringComparison.Ordinal)
            && string.Equals(FriendCode, other.FriendCode, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => obj is RrPlayer other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Pid, FriendCode);

    private static int ParseConnectionAmp(string? rawAmp)
    {
        if (string.IsNullOrWhiteSpace(rawAmp))
            return 0;
        return int.TryParse(rawAmp, out var parsedAmp) ? parsedAmp : 0;
    }
}
