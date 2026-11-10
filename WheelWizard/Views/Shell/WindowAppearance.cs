using Avalonia.Controls;
using Avalonia.Threading;
using WheelWizard.Settings;

namespace WheelWizard.Views.Shell;

/// <summary>Publishes window appearance settings as normal Avalonia resources.</summary>
public sealed class WindowAppearance(ISettingsManager settings, ISettingsSignalBus signals) : IDisposable
{
    public const string ScaleResourceKey = "WindowScale";
    public const string ControlAnimationDurationResourceKey = "ControlAnimationDuration";
    public const string SegmentAnimationDurationResourceKey = "SegmentAnimationDuration";
    public const string ControlAnimationsEnabledResourceKey = "ControlAnimationsEnabled";
    private IDisposable? _subscription;
    private int _generation;

    public void Install(IResourceDictionary resources)
    {
        Dispose();
        var generation = _generation;
        resources[ScaleResourceKey] = settings.Get<double>(settings.WINDOW_SCALE);
        UpdateAnimationDuration();
        _subscription = signals.Subscribe(signal =>
        {
            if (signal.Setting != settings.WINDOW_SCALE && signal.Setting != settings.ENABLE_ANIMATIONS)
                return;
            void Update()
            {
                if (generation == _generation)
                {
                    resources[ScaleResourceKey] = settings.Get<double>(settings.WINDOW_SCALE);
                    UpdateAnimationDuration();
                }
            }
            if (Dispatcher.UIThread.CheckAccess())
                Update();
            else
                Dispatcher.UIThread.Post(Update);
        });

        void UpdateAnimationDuration()
        {
            resources[ControlAnimationDurationResourceKey] = settings.ENABLE_ANIMATIONS.Get()
                ? TimeSpan.FromMilliseconds(120)
                : TimeSpan.Zero;
            resources[SegmentAnimationDurationResourceKey] = settings.ENABLE_ANIMATIONS.Get()
                ? TimeSpan.FromMilliseconds(240)
                : TimeSpan.Zero;
            resources[ControlAnimationsEnabledResourceKey] = settings.ENABLE_ANIMATIONS.Get();
        }
    }

    public void Dispose()
    {
        ++_generation;
        _subscription?.Dispose();
        _subscription = null;
    }
}
