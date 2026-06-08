namespace WheelWizard.RrRooms;

public sealed class RwfcRoomStatusRoom
{
    public required string Id { get; set; }

    public required string Type { get; set; }
    public required DateTime Created { get; set; }

    public string? Host { get; set; }
    public string? Rk { get; set; }
    public string? RoomType { get; set; }

    public required List<RwfcRoomStatusPlayer> Players { get; set; }

    public RwfcRoomStatusRace? Race { get; set; }

    public bool Suspend { get; set; }
}

public sealed class RwfcRoomStatusRace
{
    public int Num { get; set; }
    public int Course { get; set; }
    public int Cc { get; set; }
    public string? TrackName { get; set; }
}
