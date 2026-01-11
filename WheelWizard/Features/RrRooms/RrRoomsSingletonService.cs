using WheelWizard.Shared.Services;

namespace WheelWizard.RrRooms;

public interface IRrRoomsSingletonService
{
    Task<OperationResult<List<RwfcRoomStatusRoom>>> GetRoomsAsync();
}

public class RrRoomsSingletonService(IApiCaller<IRwfcApi> apiCaller) : IRrRoomsSingletonService
{
    public async Task<OperationResult<List<RwfcRoomStatusRoom>>> GetRoomsAsync()
    {
        var result = await apiCaller.CallApiAsync(rwfcApi => rwfcApi.GetRoomStatusAsync());
        if (result.IsFailure)
            return result.Error;

        return result.Value.Rooms;
    }
}
