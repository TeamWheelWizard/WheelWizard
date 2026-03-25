namespace WheelWizard.MiiRendering.Services;

public interface IMiiRenderingResourceLocator
{
    OperationResult<string> GetFflResourcePath();
}
