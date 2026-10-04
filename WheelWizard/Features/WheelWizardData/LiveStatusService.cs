using Microsoft.Extensions.Logging;
using WheelWizard.Shared.Polling;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.WheelWizardData;

public class LiveStatusService : ObservablePollingService
{
    private readonly IWhWzDataSingletonService _whWzDataService;
    private readonly ILogger<LiveStatusService> _logger;

    public WhWzStatus? Status { get; private set; }

    public LiveStatusService(
        IWhWzDataSingletonService whWzDataService,
        ILogger<LiveStatusService> logger,
        IPollingScheduler scheduler,
        TimeProvider timeProvider
    )
        : base(90, scheduler, timeProvider, logger)
    {
        _whWzDataService = whWzDataService;
        _logger = logger;
    }

    protected override async Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        var statusResult = await _whWzDataService.GetStatusAsync().WaitAsync(cancellationToken);
        // Stop may run after the request completes but before this continuation.
        cancellationToken.ThrowIfCancellationRequested();

        if (statusResult.IsSuccess)
        {
            Status = statusResult.Value;
            return;
        }

        _logger.LogError(statusResult.Error.Exception, "Failed to retrieve WhWz Status: {Message}", statusResult.Error.Message);
        Status = new() { Variant = WhWzStatusVariant.Error, Message = "Failed to retrieve Wheel Wizard status" };
    }
}
