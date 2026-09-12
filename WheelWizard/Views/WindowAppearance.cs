using Avalonia.Controls;
using Avalonia.Threading;
using WheelWizard.Settings;

namespace WheelWizard.Views;

/// <summary>Publishes window appearance settings as normal Avalonia resources.</summary>
public sealed class WindowAppearance(ISettingsManager settings, ISettingsSignalBus signals) : IDisposable
{
    public const string ScaleResourceKey = "WindowScale";
    private IDisposable? _subscription;
    private int _generation;

    public void Install(IResourceDictionary resources)
    {
        Dispose();
        var generation = _generation;
        resources[ScaleResourceKey] = settings.Get<double>(settings.WINDOW_SCALE);
        _subscription = signals.Subscribe(signal =>
        {
            if (signal.Setting != settings.WINDOW_SCALE)
                return;
            void Update()
            {
                if (generation == _generation)
                    resources[ScaleResourceKey] = settings.Get<double>(settings.WINDOW_SCALE);
            }
            if (Dispatcher.UIThread.CheckAccess())
                Update();
            else
                Dispatcher.UIThread.Post(Update);
        });
    }

    public void Dispose()
    {
        ++_generation;
        _subscription?.Dispose();
        _subscription = null;
    }
}
