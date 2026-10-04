using Microsoft.Extensions.Logging;

namespace WheelWizard.Shared.Polling;

/// <summary>Owns one polling lifetime. Start, stop and subscriptions use the scheduler's context.</summary>
public abstract class ObservablePollingService(
    double intervalSeconds,
    IPollingScheduler scheduler,
    TimeProvider timeProvider,
    ILogger logger
) : IDisposable
{
    private readonly List<IPollingListener> _subscribers = [];
    private IDisposable? _schedule;
    private CancellationTokenSource? _cancellation;
    private DateTimeOffset _nextTick;
    private int _executing;
    private bool _disposed;
    protected int SubscriberCount => _subscribers.Count;
    public double IntervalSeconds { get; } = intervalSeconds;
    public TimeSpan TimeUntilNextTick => _schedule == null ? TimeSpan.Zero : _nextTick - timeProvider.GetUtcNow();

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_schedule != null)
            return;
        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var token = cancellation.Token;
        _nextTick = timeProvider.GetUtcNow().AddSeconds(IntervalSeconds);
        try
        {
            _schedule = scheduler.Schedule(TimeSpan.FromSeconds(IntervalSeconds), () => ExecuteAndNotifyAsync(token));
        }
        catch
        {
            _cancellation = null;
            cancellation.Dispose();
            throw;
        }
        _ = ExecuteAndNotifyAsync(token);
    }

    public void Stop()
    {
        _cancellation?.Cancel();
        _schedule?.Dispose();
        _schedule = null;
        _cancellation?.Dispose();
        _cancellation = null;
    }

    public virtual bool Unsubscribe(IPollingListener subscriber) => _subscribers.Remove(subscriber);

    public virtual void Subscribe(IPollingListener subscriber)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_subscribers.Contains(subscriber))
            _subscribers.Add(subscriber);
    }

    private async Task ExecuteAndNotifyAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || Interlocked.CompareExchange(ref _executing, 1, 0) != 0)
            return;
        try
        {
            await ExecuteTaskAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
                return;
            // A listener may remove itself or another listener while handling the update.
            foreach (var subscriber in _subscribers.ToArray())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                if (!_subscribers.Contains(subscriber))
                    continue;
                try
                {
                    subscriber.OnUpdate(this);
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Polling listener failed for {Service}", GetType().Name);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            logger.LogError(exception, "Polling failed for {Service}", GetType().Name);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
                _nextTick = timeProvider.GetUtcNow().AddSeconds(IntervalSeconds);
            Volatile.Write(ref _executing, 0);
        }
    }

    protected abstract Task ExecuteTaskAsync(CancellationToken cancellationToken);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Stop();
        _subscribers.Clear();
        GC.SuppressFinalize(this);
    }
}
