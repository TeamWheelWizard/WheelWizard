namespace WheelWizard.RrRooms;

public sealed class RwfcRoomStatusResponse
{
    public required List<RwfcRoomStatusRoom> Rooms { get; set; }

    //todo: figure out what these refer to:
    public DateTime Timestamp { get; set; }

    public int? Id { get; set; }
    public int? MinimumId { get; set; }
    public int? MaximumId { get; set; }
}
