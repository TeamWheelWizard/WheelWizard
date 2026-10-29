using WheelWizard.Shared.Services;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData;

public interface IWhWzDataSingletonService
{
    Task<OperationResult<WhWzStatus>> GetStatusAsync();
}

public class WhWzDataSingletonService(IApiCaller<IWhWzDataApi> apiCaller) : IWhWzDataSingletonService
{
    public async Task<OperationResult<WhWzStatus>> GetStatusAsync()
    {
        return await apiCaller.CallApiAsync(whWzDataApi => whWzDataApi.GetStatusAsync());
    }
}
