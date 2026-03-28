using Microsoft.Extensions.Logging;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.Services.LiveData;

public class WhWzStatusManager : RepeatedTaskManager
{
    private readonly IWhWzDataSingletonService _whWzDataService;
    private readonly ILogger<WhWzStatusManager> _logger;

    public WhWzStatus? Status { get; private set; }

    public static WhWzStatusManager Instance => App.Services.GetRequiredService<WhWzStatusManager>();

    public WhWzStatusManager(IWhWzDataSingletonService whWzDataService, ILogger<WhWzStatusManager> logger)
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
