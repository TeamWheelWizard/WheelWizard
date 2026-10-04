using WheelWizard.RrRooms;
using WheelWizard.WheelWizardData;

namespace WheelWizard.ApplicationLifecycle;

public interface IApplicationLiveUpdates
{
    void Start();
}

public sealed class ApplicationLiveUpdates(LiveStatusService status, LiveRoomsService rooms) : IApplicationLiveUpdates
{
    public void Start()
    {
        status.Start();
        rooms.Start();
    }
}
