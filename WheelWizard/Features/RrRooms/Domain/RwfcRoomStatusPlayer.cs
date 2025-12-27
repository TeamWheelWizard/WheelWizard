namespace WheelWizard.RrRooms;

public sealed class RwfcRoomStatusPlayer
{
    public required string Pid { get; set; }
    public string Name { get; set; } = string.Empty;

    public string FriendCode { get; set; } = string.Empty;

    public int? Vr { get; set; }
    public int? Br { get; set; }

    public bool IsOpenHost { get; set; }
    public bool IsSuspended { get; set; }

    public RwfcRoomStatusMii? Mii { get; set; }

    public List<string> ConnectionMap { get; set; } = [];
}
