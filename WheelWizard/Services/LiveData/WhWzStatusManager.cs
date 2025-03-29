using Microsoft.Extensions.Logging;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;

namespace WheelWizard.Services.LiveData;

public class WhWzStatusManager : RepeatedTaskManager
{
    public WhWzStatus? Status { get; private set; }
    
    private static WhWzStatusManager? _instance;
    public static WhWzStatusManager Instance => _instance ??= new WhWzStatusManager();

    private WhWzStatusManager() : base(90) { }

    protected override async Task ExecuteTaskAsync()
    {
        var whWzDataService = App.Services.GetRequiredService<IWhWzDataSingletonService>();
        var statusResult = await whWzDataService.GetStatusAsync();

        if (statusResult.IsSuccess)
        {
            Status = statusResult.Value;
            return;
        }
        
        App.Services.GetRequiredService<ILogger<WhWzStatusManager>>()
            .LogError(statusResult.Error.Exception, "Failed to retrieve WhWz Status: {Message}", statusResult.Error.Message);
        Status = new WhWzStatus {Variant = WhWzStatusVariant.Error, Message = "Failed to retrieve Wheel Wizard status"};
    }
}
