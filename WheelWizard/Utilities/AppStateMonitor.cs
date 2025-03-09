using WheelWizard.Utilities.RepeatedTasks;

namespace WheelWizard.Utilities;

public class AppStateMonitor : RepeatedTaskManager
{
    //public static int RandomNumber = 1;
    private AppStateMonitor() : base(0.2) { }
    
    private static AppStateMonitor? _instance;
    public static AppStateMonitor Instance => _instance ??= new AppStateMonitor();
    
    protected override Task ExecuteTaskAsync()
    {
        // If you want to calculate something each time you can do that here, for example:
        // But if not, this method is also already useful to just notify the subscribers every x seconds
        
        // RandomNumber++;
        return Task.CompletedTask;
    }

    public override bool Unsubscribe(IRepeatedTaskListener subscriber)
    {
        var unSubbed = base.Unsubscribe(subscriber);
        if (SubscriberCount == 0)
            Stop();
        return unSubbed;
    }
    
    public override void Subscribe(IRepeatedTaskListener subscriber)
    {
        if (SubscriberCount == 0)
        { // If this is the first subscriber, start the timer
            Start();
            NotifySubscribers(); // quick notification to instantly update the UI
        }
        base.Subscribe(subscriber);
    }
}
