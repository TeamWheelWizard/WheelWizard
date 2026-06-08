using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Models.RRInfo;

public class RrRoom
{
    public required string Id { get; set; }
    public required DateTime Created { get; set; }
    public required string Type { get; set; }
    public required bool Suspend { get; set; }
    public string? RoomType { get; set; }
    public string? CurrentTrackName { get; set; }
    public required List<RrPlayer> Players { get; set; }

    public int PlayerCount => Players.Count;
    public string RoomStatusText => Suspend ? "Voting" : CurrentTrackName ?? string.Empty;

    public string TimeOnline => tTime(DateTime.UtcNow - Created);
    public bool IsPublic => Type != "private";

    public string GameMode =>
        !string.IsNullOrWhiteSpace(RoomType) ? RoomType
        : IsPublic ? "Unknown Mode"
        : "Private Room";

    public int AverageVr
    {
        get
        {
            var vrs = Players.Select(p => p.Vr).Where(v => v.HasValue).Select(v => v!.Value).ToList();
            return vrs.Count == 0 ? 0 : (int)vrs.Average();
        }
    }

    public RrPlayer? HostPlayer => Players.FirstOrDefault(p => p.IsOpenHost);

    public Mii? HostMii => HostPlayer?.FirstMii;
}
