using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.Shared.Polling;
using WheelWizard.Views.Diagnostics;

namespace WheelWizard.Test.Shared.Polling;

public class ObservablePollingServiceTests
{
    [Fact]
    public async Task SlowPoll_DoesNotOverlap_AndStopSuppressesItsNotification()
    {
        var scheduler = new ManualScheduler();
        using var service = new TestPolling(scheduler);
        var listener = Substitute.For<IPollingListener>();
        service.Subscribe(listener);
        service.Start();
        listener.ClearReceivedCalls();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Execute = _ => pending.Task;

        var tick = scheduler.Schedules[0].Fire();
        await scheduler.Schedules[0].Fire();
        Assert.Equal(2, service.ExecutionCount); // Initial execution plus one in-flight poll.
        service.Stop();
        pending.SetResult();
        await tick;

        Assert.True(scheduler.Schedules[0].Disposed);
        listener.DidNotReceive().OnUpdate(service);
    }

    [Fact]
    public async Task StoppedCallback_CannotRunAfterRestart_OrStopAnotherOwner()
    {
        var scheduler = new ManualScheduler();
        using var first = new TestPolling(scheduler);
        using var second = new TestPolling(scheduler);
        first.Start();
        second.Start();
        first.Stop();
        first.Start();

        await scheduler.Schedules[0].Fire(); // An already queued callback from the old lifetime.
        await scheduler.Schedules[1].Fire();

        Assert.Equal(2, first.ExecutionCount);
        Assert.Equal(2, second.ExecutionCount);
        Assert.False(scheduler.Schedules[1].Disposed);
        Assert.False(scheduler.Schedules[2].Disposed);
    }

    [Fact]
    public async Task FailedPoll_IsObserved_AndNextTickCanRecover()
    {
        var scheduler = new ManualScheduler();
        using var service = new TestPolling(scheduler);
        var listener = Substitute.For<IPollingListener>();
        service.Subscribe(listener);
        service.Execute = _ => throw new IOException("offline");
        service.Start();
        listener.DidNotReceive().OnUpdate(service);
        service.Execute = _ => Task.CompletedTask;

        await scheduler.Schedules[0].Fire();

        listener.Received(1).OnUpdate(service);
    }

    [Fact]
    public async Task ListenerMayUnsubscribeAndThrow_WithoutBreakingOtherListeners()
    {
        var scheduler = new ManualScheduler();
        using var service = new TestPolling(scheduler);
        var first = Substitute.For<IPollingListener>();
        var second = Substitute.For<IPollingListener>();
        first
            .When(listener => listener.OnUpdate(service))
            .Do(_ =>
            {
                service.Unsubscribe(first);
                throw new InvalidOperationException();
            });
        service.Subscribe(first);
        service.Subscribe(second);
        service.Start();

        await scheduler.Schedules[0].Fire();

        first.Received(1).OnUpdate(service);
        second.Received(2).OnUpdate(service);
    }

    [Fact]
    public async Task Dispose_CancelsRequestAndRejectsRestart()
    {
        var scheduler = new ManualScheduler();
        var service = new TestPolling(scheduler);
        service.Start();
        CancellationToken requestToken = default;
        service.Execute = token =>
        {
            requestToken = token;
            return Task.Delay(Timeout.Infinite, token);
        };
        var tick = scheduler.Schedules[0].Fire();

        service.Dispose();
        service.Dispose();
        await tick;

        Assert.True(requestToken.IsCancellationRequested);
        Assert.True(scheduler.Schedules[0].Disposed);
        Assert.Throws<ObjectDisposedException>(service.Start);
    }

    [Fact]
    public void Countdown_UsesInjectedClock_AndResetsWhenStopped()
    {
        var scheduler = new ManualScheduler();
        var clock = new TestClock();
        using var service = new TestPolling(scheduler, clock);
        service.Start();
        clock.Now += TimeSpan.FromSeconds(7);
        Assert.Equal(TimeSpan.FromSeconds(33), service.TimeUntilNextTick);
        service.Stop();
        Assert.Equal(TimeSpan.Zero, service.TimeUntilNextTick);
    }

    [Fact]
    public void DevelopmentRefresh_RunsOnlyWhileSubscribed_AndUpdatesFirstSubscriber()
    {
        var scheduler = new ManualScheduler();
        using var service = new DevelopmentRefreshService(scheduler, TimeProvider.System, NullLogger<DevelopmentRefreshService>.Instance);
        var listener = Substitute.For<IPollingListener>();
        service.Subscribe(listener);
        listener.Received(1).OnUpdate(service);
        service.Subscribe(listener);
        Assert.Single(scheduler.Schedules);
        service.Unsubscribe(listener);
        Assert.True(scheduler.Schedules[0].Disposed);
    }

    private sealed class TestPolling(ManualScheduler scheduler, TimeProvider? clock = null)
        : ObservablePollingService(40, scheduler, clock ?? TimeProvider.System, NullLogger.Instance)
    {
        public Func<CancellationToken, Task> Execute { get; set; } = _ => Task.CompletedTask;
        public int ExecutionCount { get; private set; }

        protected override Task ExecuteTaskAsync(CancellationToken cancellationToken)
        {
            ExecutionCount++;
            return Execute(cancellationToken);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class ManualScheduler : IPollingScheduler
    {
        public List<ScheduleHandle> Schedules { get; } = [];

        public IDisposable Schedule(TimeSpan interval, Func<Task> callback)
        {
            var handle = new ScheduleHandle(callback);
            Schedules.Add(handle);
            return handle;
        }

        public sealed class ScheduleHandle(Func<Task> callback) : IDisposable
        {
            public bool Disposed { get; private set; }

            public Task Fire() => callback();

            public void Dispose() => Disposed = true;
        }
    }
}
