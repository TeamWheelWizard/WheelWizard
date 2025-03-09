using Avalonia.Threading;

namespace WheelWizard.Utilities.RepeatedTasks;

public abstract class RepeatedTaskManager
{
    protected int SubscriberCount => _subscribers.Count;
    public readonly double IntervalSeconds;
    private readonly List<IRepeatedTaskListener> _subscribers = new();
    private DispatcherTimer? _timer;
    private DateTime _nextTick;
    
    public TimeSpan TimeUntilNextTick => _nextTick - DateTime.Now;
    
    // Since we are using DispatcherTimer, we cant use CancellationTokenSource, so we just do it with a bool ¯\(°_o)/¯
    private static bool _globalCancellation;

    protected RepeatedTaskManager(double intervalSeconds)
    {
        IntervalSeconds = intervalSeconds;
    }

    protected void NotifySubscribers()
    {
        foreach (var subscriber in _subscribers)
            subscriber.OnUpdate(this);
    }

    public void Start()
    {
        if (_globalCancellation || _timer != null) return;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(IntervalSeconds)
        };
        
        _nextTick = DateTime.Now.AddSeconds(IntervalSeconds);
        
        _timer.Tick += async (_, _) =>
        {
            await ExecuteAndNotifyAsync();
            _nextTick = DateTime.Now.AddSeconds(IntervalSeconds);
        };

        _timer.Start();

        // Run the initial execution
        ExecuteAndNotifyAsync();
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    public virtual bool Unsubscribe(IRepeatedTaskListener subscriber) => _subscribers.Remove(subscriber);
    public virtual void Subscribe(IRepeatedTaskListener subscriber)
    {
        if (!_subscribers.Contains(subscriber))
            _subscribers.Add(subscriber);
    }

    private async Task ExecuteAndNotifyAsync()
    {
        if (_globalCancellation)
        {
            Stop();
            return;
        }

        await ExecuteTaskAsync();
        NotifySubscribers();
    }

    protected abstract Task ExecuteTaskAsync();

    // This is used to stop all tasks, regardless of what manager it is, this can be used when the application
    // is closing for example.  It is different then the Stop method, because that only stops the current task.
    public static void CancelAllTasks() => _globalCancellation = true;
}
