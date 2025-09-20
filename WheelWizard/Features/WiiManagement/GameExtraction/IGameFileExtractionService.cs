using System.Threading;
using System.Threading.Tasks;
using WheelWizard.Shared;

namespace WheelWizard.WiiManagement.GameExtraction;

public interface IGameFileExtractionService
{
    Task<OperationResult<string>> EnsureExtractedAsync(CancellationToken cancellationToken = default);

    string GetExtractionRootPath();

    string GetLaunchFilePath();
}
