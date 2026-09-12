using Microsoft.Extensions.Logging;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData;

public class LiveStatusService : RepeatedTaskManager
{
    private readonly IWhWzDataSingletonService _whWzDataService;
    private readonly ILogger<LiveStatusService> _logger;

    public WhWzStatus? Status { get; private set; }

    public LiveStatusService(IWhWzDataSingletonService whWzDataService, ILogger<LiveStatusService> logger)
        : base(90)
    {
        _whWzDataService = whWzDataService;
        _logger = logger;
    }

    protected override async Task ExecuteTaskAsync()
    {
        var statusResult = await _whWzDataService.GetStatusAsync();

        if (statusResult.IsSuccess)
        {
            Status = statusResult.Value;
            return;
        }

        _logger.LogError(statusResult.Error.Exception, "Failed to retrieve WhWz Status: {Message}", statusResult.Error.Message);
        Status = new() { Variant = WhWzStatusVariant.Error, Message = "Failed to retrieve Wheel Wizard status" };
    }
}
