using WheelWizard.Helpers;
using WheelWizard.Utilities.RepeatedTasks;

namespace WheelWizard.Services.LiveData;

public class LiveAlertsManager : RepeatedTaskManager
{
    public LiveStatus? Status { get; private set; }
    
    private static LiveAlertsManager? _instance;
    public static LiveAlertsManager Instance => _instance ??= new LiveAlertsManager();

    private LiveAlertsManager() : base(90) { }

    protected override async Task ExecuteTaskAsync()
    {
        var response = await HttpClientHelper.GetAsync<LiveStatus>(Endpoints.WhWzStatusUrl);
        Status = response.Content ?? new()
        {
            Message = "Can't connect to the servers. \nYou might experience internet connection issues.", 
            Variant = "warning"
        };
    }

    public class LiveStatus
    {
        public required string Variant { get; set; }
        public required string Message { get; set; }
        public LiveStatusVariant StatusVariant => Variant.ToLower() switch
            {
                "warning" => LiveStatusVariant.Warning,
                "error" => LiveStatusVariant.Error,
                "success" => LiveStatusVariant.Success,
                "info" => LiveStatusVariant.Info,
                "party" => LiveStatusVariant.Party,
                "question" => LiveStatusVariant.Question,
                _ => LiveStatusVariant.None
            };
    }

    public enum LiveStatusVariant
    {
        None,
        Warning,
        Error,
        Success,
        Info,
        Party,
        Question
    }
}
