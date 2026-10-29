namespace WheelWizard.RrRooms;

public sealed class RwfcBadgesResponse
{
    // Keyed by profile id (pid); values are RWFC badge ids.
    public Dictionary<string, int[]?> Badges { get; set; } = [];
}
