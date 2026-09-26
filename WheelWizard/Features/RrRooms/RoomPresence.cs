namespace WheelWizard.RrRooms;

public interface IRoomPresence
{
    bool IsOnline(string friendCode);
    void Replace(IEnumerable<string> onlineFriendCodes);
}

public sealed class RoomPresence : IRoomPresence
{
    private HashSet<string> _onlineFriendCodes = new(StringComparer.Ordinal);

    public bool IsOnline(string friendCode) => Volatile.Read(ref _onlineFriendCodes).Contains(friendCode);

    public void Replace(IEnumerable<string> onlineFriendCodes) =>
        Volatile.Write(
            ref _onlineFriendCodes,
            onlineFriendCodes.Where(code => !string.IsNullOrWhiteSpace(code)).ToHashSet(StringComparer.Ordinal)
        );
}
