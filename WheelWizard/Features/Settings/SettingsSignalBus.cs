using WheelWizard.Settings.Domain;

namespace WheelWizard.Settings;

public readonly record struct SettingChangedSignal(Setting Setting);

public interface ISettingsSignalBus
{
    IDisposable Subscribe(Action<SettingChangedSignal> handler);
    void Publish(Setting setting);
}

public sealed class SettingsSignalBus : ISettingsSignalBus
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<long, Action<SettingChangedSignal>> _subscribers = [];
    private long _nextSubscriberId;

    public IDisposable Subscribe(Action<SettingChangedSignal> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        long id;
        lock (_syncRoot)
        {
            id = _nextSubscriberId++;
            _subscribers[id] = handler;
        }

        return new Subscription(this, id);
    }

    public void Publish(Setting setting)
    {
        Action<SettingChangedSignal>[] handlers;
        lock (_syncRoot)
        {
            handlers = [.. _subscribers.Values];
        }

        var signal = new SettingChangedSignal(setting);
        foreach (var handler in handlers)
        {
            handler(signal);
        }
    }

    private void Unsubscribe(long subscriberId)
    {
        lock (_syncRoot)
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
