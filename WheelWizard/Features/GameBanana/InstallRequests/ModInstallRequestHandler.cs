namespace WheelWizard.GameBanana.InstallRequests;

public interface IModInstallRequestPresentation
{
    Task ShowModAsync(ModInstallRequest request);
    void ShowError(string reason);
}

public interface IModInstallRequestHandler
{
    Task<OperationResult> HandleAsync(string url);
}

public sealed class ModInstallRequestHandler(IModInstallRequestPresentation presentation) : IModInstallRequestHandler
{
    public async Task<OperationResult> HandleAsync(string url)
    {
        var request = ModInstallRequest.Parse(url);
        if (request.IsFailure)
        {
            presentation.ShowError(request.Error.Message);
            return request.Error;
        }
        try
        {
            await presentation.ShowModAsync(request.Value);
            return Ok();
        }
        catch (Exception exception)
        {
            presentation.ShowError(exception.Message);
            return exception;
        }
    }
}
