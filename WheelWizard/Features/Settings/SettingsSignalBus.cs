using Microsoft.Extensions.Logging;
using WheelWizard.Settings.Types;

namespace WheelWizard.Settings;

public readonly record struct SettingChangedSignal(Setting Setting);

public interface ISettingsSignalBus
{
    IDisposable Subscribe(Action<SettingChangedSignal> handler);
    void Publish(Setting setting);
}

public sealed class SettingsSignalBus(ILogger<SettingsSignalBus> logger) : ISettingsSignalBus
{
    // LOCKS:
    // We are working with locks. This is to ensure that we always have accurate information in our settings / application.
    // We do not create multiple threads. However, some of our features run through Tasks. Those are executed asynchronously, therefore still require locks.

    private readonly object _syncSubscribers = new();
    private readonly Dictionary<long, Action<SettingChangedSignal>> _subscribers = [];
    private long _nextSubscriberId;

    public IDisposable Subscribe(Action<SettingChangedSignal> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        long id;
        lock (_syncSubscribers)
        {
            id = _nextSubscriberId++;
            _subscribers[id] = handler;
        }

        return new Subscription(this, id);
    }

    public void Publish(Setting setting)
    {
        Action<SettingChangedSignal>[] handlers;

        // You could use a lock for reading the subscribes. But let's minimize the lock usage to where it is important.
        // If the handlers list is slightly outdated it is not a problem (unlike when this happens when modifying this list)
        handlers = [.. _subscribers.Values];

        var signal = new SettingChangedSignal(setting);
        foreach (var handler in handlers)
        {
            try
            {
                handler(signal);
            }
            catch
            {
                // Exceptions from subscribers should not affect the publisher or other subscribers, so we catch and log them.
                logger.LogError("A subscriber threw an exception while handling a setting changed signal.");
            }
        }
    }

    private void Unsubscribe(long subscriberId)
    {
        lock (_syncSubscribers)
        {
            _subscribers.Remove(subscriberId);
        }
    }

    private sealed class Subscription(SettingsSignalBus bus, long subscriberId) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            bus.Unsubscribe(subscriberId);
            _disposed = true;
        }
    }
}
