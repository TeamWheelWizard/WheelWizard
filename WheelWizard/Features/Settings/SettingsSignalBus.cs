using WheelWizard.Models.Settings;

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
        if (handler == null)
            throw new ArgumentNullException(nameof(handler));

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

public static class SettingsSignalRuntime
{
    private static readonly object SyncRoot = new();
    private static ISettingsSignalBus? _current;
    private static readonly List<Action<ISettingsSignalBus>> PendingInitializers = [];

    public static void Initialize(ISettingsSignalBus signalBus)
    {
        ArgumentNullException.ThrowIfNull(signalBus);

        List<Action<ISettingsSignalBus>> callbacksToRun;
        lock (SyncRoot)
        {
            _current = signalBus;
            callbacksToRun = [.. PendingInitializers];
            PendingInitializers.Clear();
        }

        foreach (var callback in callbacksToRun)
        {
            callback(signalBus);
        }
    }

    public static void OnInitialized(Action<ISettingsSignalBus> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);

        ISettingsSignalBus? signalBus;
        lock (SyncRoot)
        {
            signalBus = _current;
            if (signalBus == null)
            {
                PendingInitializers.Add(callback);
                return;
            }
        }

        callback(signalBus);
    }

    public static void Publish(Setting setting)
    {
        ISettingsSignalBus? signalBus;
        lock (SyncRoot)
        {
            signalBus = _current;
        }

        signalBus?.Publish(setting);
    }
}
