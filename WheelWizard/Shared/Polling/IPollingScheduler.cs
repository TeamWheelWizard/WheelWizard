namespace WheelWizard.Shared.Polling;

/// <summary>Schedules callbacks on the owning presentation context.</summary>
public interface IPollingScheduler
{
    IDisposable Schedule(TimeSpan interval, Func<Task> callback);
}

public interface IPollingListener
{
    void OnUpdate(ObservablePollingService sender);
}
