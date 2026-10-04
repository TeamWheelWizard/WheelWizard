using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.RrRooms;
using WheelWizard.Shared;
using WheelWizard.Shared.Polling;
using WheelWizard.WheelWizardData;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.Test.Shared.Polling;

public class LivePollingCancellationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StoppedRooms_DoNotPublishCompletedOrPendingRequest(bool completeBeforeStop, bool dispose)
    {
        var scheduler = new ManualScheduler();
        var rooms = Substitute.For<IRrRoomsSingletonService>();
        var rankings = Substitute.For<IRrLeaderboardSingletonService>();
        rankings.GetTopPlayersAsync(50).Returns(Ok(new List<RwfcLeaderboardEntry>()));
        var licenses = Substitute.For<IGameLicenseSingletonService>();
        licenses.ActiveCurrentFriends.Returns([]);
        var presence = new RoomPresence();
        var completion = new TaskCompletionSource<OperationResult<List<RwfcRoomStatusRoom>>>();
        rooms.GetRoomsAsync().Returns(completion.Task);
        using var service = new LiveRoomsService(
            Substitute.For<IWhWzDataSingletonService>(),
            rooms,
            rankings,
            licenses,
            presence,
            scheduler,
            TimeProvider.System,
            NullLogger<LiveRoomsService>.Instance
        );
        var listener = Substitute.For<IPollingListener>();
        service.Subscribe(listener);
        var ui = new QueuedContext();
        ui.Run(service.Start);
        var result = Ok(
            new List<RwfcRoomStatusRoom>
            {
                new()
                {
                    Id = "late",
                    Type = "public",
                    Created = DateTime.UtcNow,
                    Players = [new() { Pid = "1", FriendCode = "late player" }],
                },
            }
        );
        if (completeBeforeStop)
            CompleteOutsideContext(() => completion.SetResult(result));
        if (dispose)
            ui.Run(service.Dispose);
        else
            ui.Run(service.Stop);
        if (!completeBeforeStop)
            CompleteOutsideContext(() => completion.SetResult(result));
        ui.Drain();

        listener.DidNotReceive().OnUpdate(service);
        Assert.Empty(service.CurrentRooms);
        Assert.False(presence.IsOnline("late player"));
        licenses.DidNotReceive().RefreshOnlineStatus();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StoppedStatus_DoesNotPublishCompletedOrPendingRequest(bool completeBeforeStop, bool dispose)
    {
        var scheduler = new ManualScheduler();
        var data = Substitute.For<IWhWzDataSingletonService>();
        var completion = new TaskCompletionSource<OperationResult<WhWzStatus>>();
        data.GetStatusAsync().Returns(completion.Task);
        using var service = new LiveStatusService(data, NullLogger<LiveStatusService>.Instance, scheduler, TimeProvider.System);
        var listener = Substitute.For<IPollingListener>();
        service.Subscribe(listener);
        var ui = new QueuedContext();
        ui.Run(service.Start);
        var result = Ok(new WhWzStatus { Message = "late status" });
        if (completeBeforeStop)
            CompleteOutsideContext(() => completion.SetResult(result));
        if (dispose)
            ui.Run(service.Dispose);
        else
            ui.Run(service.Stop);
        if (!completeBeforeStop)
            CompleteOutsideContext(() => completion.SetResult(result));
        ui.Drain();

        listener.DidNotReceive().OnUpdate(service);
        Assert.Null(service.Status);
    }

    private static void CompleteOutsideContext(Action action)
    {
        var prior = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prior);
        }
    }

    private sealed class QueuedContext : SynchronizationContext
    {
        private readonly Queue<Action> _pending = new();

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_pending)
                _pending.Enqueue(() => callback(state));
        }

        public void Run(Action action)
        {
            var prior = Current;
            SetSynchronizationContext(this);
            try
            {
                action();
            }
            finally
            {
                SetSynchronizationContext(prior);
            }
        }

        public void Drain()
        {
            while (true)
            {
                Action? action;
                lock (_pending)
                {
                    if (!_pending.TryDequeue(out action))
                        return;
                }
                Run(action);
            }
        }
    }

    private sealed class ManualScheduler : IPollingScheduler, IDisposable
    {
        public Func<Task>? Callback;
        public bool Disposed;

        public IDisposable Schedule(TimeSpan interval, Func<Task> callback)
        {
            Callback = callback;
            return this;
        }

        public void Dispose() => Disposed = true;
    }
}
