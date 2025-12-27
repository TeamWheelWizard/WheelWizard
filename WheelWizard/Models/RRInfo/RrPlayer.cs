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

    public List<string> ConnectionMap { get; set; } = [];

    public Mii? Mii { get; set; }

    public Mii? FirstMii => Mii;

    public string VrDisplay => Vr?.ToString() ?? "--";
    public string BrDisplay => Br?.ToString() ?? "--";

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
}
