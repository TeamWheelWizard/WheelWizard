using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.CustomDistributions;

namespace WheelWizard.Test.Features.CustomDistributions;

public class RetroRewindBackgroundInstallTests
{
    [Fact]
    public async Task Start_RunsOneUnattendedInstall_AndReportsCompletion()
    {
        var distribution = Substitute.For<IDistribution>();
        DistributionOperation? seen = null;
        distribution
            .UpdateAsync(Arg.Do<DistributionOperation>(operation => seen = operation))
            .Returns(_ =>
            {
                seen!.Report(new(Percent: 40));
                return Task.FromResult(Ok());
            });
        var install = Create(distribution, new ImmediateTimeProvider());
        var percents = new List<int>();
        install.Changed += (_, _) => percents.Add(install.Percent);

        var first = install.StartAsync();
        var second = install.StartAsync();

        Assert.True((await first).IsSuccess);
        Assert.Same(first, second);
        Assert.True(seen!.Unattended);
        Assert.Contains(40, percents);
        Assert.Equal(BackgroundInstallState.Completed, install.State);
        await distribution.Received(1).UpdateAsync(Arg.Any<DistributionOperation>());
    }

    [Fact]
    public async Task Failure_RetriesWithBackoff_ThenWaitsForAManualRetry()
    {
        var distribution = Substitute.For<IDistribution>();
        distribution.UpdateAsync(Arg.Any<DistributionOperation>()).Returns(Fail("offline"));
        var install = Create(distribution, new ImmediateTimeProvider());

        Assert.True((await install.StartAsync()).IsFailure);
        Assert.Equal(BackgroundInstallState.Failed, install.State);
        Assert.Equal("offline", install.ErrorMessage);
        await distribution.Received(3).UpdateAsync(Arg.Any<DistributionOperation>());

        distribution.UpdateAsync(Arg.Any<DistributionOperation>()).Returns(Ok());
        Assert.True((await install.RetryAsync()).IsSuccess);
        Assert.Equal(BackgroundInstallState.Completed, install.State);
    }

    private static RetroRewindBackgroundInstall Create(IDistribution distribution, TimeProvider time)
    {
        var distributions = Substitute.For<ICustomDistributionSingletonService>();
        distributions.RetroRewind.Returns(distribution);
        return new RetroRewindBackgroundInstall(distributions, time, NullLogger<RetroRewindBackgroundInstall>.Instance);
    }

    /// <summary>Lets the retry backoff elapse at once.</summary>
    private sealed class ImmediateTimeProvider : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new NoTimer();
        }

        private sealed class NoTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose() { }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
