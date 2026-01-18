namespace WheelWizard.RrRooms;

public sealed class RwfcRoomStatusRoom
{
    public required string Id { get; set; }

    public required string Type { get; set; }
    public required DateTime Created { get; set; }

    public string? Host { get; set; }
    public string? Rk { get; set; }

    public required List<RwfcRoomStatusPlayer> Players { get; set; }

    public bool Suspend { get; set; }
}
