using WheelWizard.Helpers;
using WheelWizard.Models.RRInfo;
using WheelWizard.Utilities.RepeatedTasks;

namespace WheelWizard.Services.LiveData;

public class RRLiveRooms : RepeatedTaskManager
{
    public List<RrRoom> CurrentRooms { get; private set; } = new();
    public int PlayerCount => CurrentRooms.Sum(room => room.PlayerCount);
    public int RoomCount => CurrentRooms.Count;

    private static RRLiveRooms? _instance;
    public static RRLiveRooms Instance => _instance ??= new RRLiveRooms();

    private RRLiveRooms() : base(40) { }

    protected override async Task ExecuteTaskAsync()
    { 
        var response = await HttpClientHelper.GetAsync<List<RrRoom>>(Endpoints.RRGroupsUrl);
        // var response = HttpClientHelper.MockResult<List<Room>>(Mocker.GroupApiResponse);

        // It is not important enough to bore the user with an error message or something.
        // they are not hindered if there is an error, they just don't see the rooms. that's it.
        if (!response.Succeeded || response.Content is null)
        {
            CurrentRooms = new List<RrRoom>();
        }
        else CurrentRooms = response.Content;
    }
}
