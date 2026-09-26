using Microsoft.Extensions.Logging;
using WheelWizard.Shared.Polling;

namespace WheelWizard.Views.Diagnostics;

public sealed class DevelopmentRefreshService(
    IPollingScheduler scheduler,
    TimeProvider timeProvider,
    ILogger<DevelopmentRefreshService> logger
) : ObservablePollingService(0.2, scheduler, timeProvider, logger)
{
    protected override Task ExecuteTaskAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public override bool Unsubscribe(IPollingListener subscriber)
    {
        var removed = base.Unsubscribe(subscriber);
        if (SubscriberCount == 0)
            Stop();
        return removed;
    }

    public override void Subscribe(IPollingListener subscriber)
    {
        var wasEmpty = SubscriberCount == 0;
        base.Subscribe(subscriber);
        if (wasEmpty)
            Start();
    }
}
