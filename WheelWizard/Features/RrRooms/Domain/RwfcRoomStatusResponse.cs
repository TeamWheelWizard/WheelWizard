namespace WheelWizard.RrRooms;

public sealed class RwfcRoomStatusResponse
{
    public required List<RwfcRoomStatusRoom> Rooms { get; set; }

    public DateTime Timestamp { get; set; }

    public int? Id { get; set; }
    public int? MinimumId { get; set; }
    public int? MaximumId { get; set; }
}
