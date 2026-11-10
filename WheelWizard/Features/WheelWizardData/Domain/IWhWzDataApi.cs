using Refit;

namespace WheelWizard.WheelWizardData.Domain;

public interface IWhWzDataApi
{
    [Get("/status.json")]
    Task<WhWzStatus> GetStatusAsync();
}
