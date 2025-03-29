using WheelWizard.Models.MiiImages;
using WheelWizard.Models.RRInfo;
using WheelWizard.RrRooms;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views;
using WheelWizard.WheelWizardData;

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
        var whWzService = App.Services.GetRequiredService<IWhWzDataSingletonService>();
        var roomsService = App.Services.GetRequiredService<IRrRoomsSingletonService>();

        var roomsResult = await roomsService.GetRoomsAsync();
        if (roomsResult.IsFailure)
        {
            CurrentRooms = [];
            return;
        }

        // This is here because we don't want to break existing code that uses the old model
        var rrRooms = roomsResult.Value.Select(room => new RrRoom
        {
            Id = room.Id,
            Game = room.Game,
            Created = room.Created,
            Type = room.Type,
            Suspend = room.Suspend,
            Host = room.Host,
            Rk = room.Rk,
            Players = room.Players.ToDictionary(p => p.Key,
                p => new RrPlayer
                {
                    Count = p.Value.Count,
                    Pid = p.Value.Pid,
                    Name = p.Value.Name,
                    ConnMap = p.Value.ConnMap,
                    ConnFail = p.Value.ConnFail,
                    Suspend = p.Value.Suspend,
                    Fc = p.Value.Fc,
                    Ev = p.Value.Ev,
                    Eb = p.Value.Eb,
                    BadgeVariants = whWzService.GetBadges(p.Value.Fc),
                    Mii = p.Value.Mii.Select(mii => new Mii
                    {
                        Name = mii.Name,
                        Data = mii.Data,
                    }).ToList()
                })
        }).ToList();

        CurrentRooms = rrRooms;
    }
}
