using Microsoft.Extensions.Logging;

namespace WheelWizard.CustomDistributions;

public enum BackgroundInstallState
{
    Idle,
    Running,
    Failed,
    Completed,
}

/// <summary>
/// Installs or updates Retro Rewind without any window, so the onboarding can keep going while it downloads
/// and Home can show the same progress afterwards. There is at most one run at a time.
/// </summary>
public interface IRetroRewindBackgroundInstall
{
    BackgroundInstallState State { get; }
    bool IsRunning { get; }
    int Percent { get; }
    string? Message { get; }
    string? ErrorMessage { get; }

    /// <summary>Raised from any thread whenever the state, percentage or message changes.</summary>
    event EventHandler? Changed;

    /// <summary>Starts a run unless one is already running or finished; returns that run.</summary>
    Task<OperationResult> StartAsync();

    /// <summary>Starts a new run after a failure.</summary>
    Task<OperationResult> RetryAsync();
}

public sealed class RetroRewindBackgroundInstall(
    ICustomDistributionSingletonService distributions,
    TimeProvider time,
    ILogger<RetroRewindBackgroundInstall> logger
) : IRetroRewindBackgroundInstall
{
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15)];
    private readonly Lock _lock = new();
    private Task<OperationResult>? _run;

    public BackgroundInstallState State { get; private set; }
    public bool IsRunning => State == BackgroundInstallState.Running;
    public int Percent { get; private set; }
    public string? Message { get; private set; }
    public string? ErrorMessage { get; private set; }
    public event EventHandler? Changed;

    public Task<OperationResult> StartAsync()
    {
        lock (_lock)
        {
            if (_run is not null && State != BackgroundInstallState.Failed)
                return _run;
            return _run = RunAsync();
        }
    }

    public Task<OperationResult> RetryAsync() => StartAsync();

    private async Task<OperationResult> RunAsync()
    {
        Update(BackgroundInstallState.Running, 0, null, null);
        OperationResult result = Fail("Retro Rewind could not be installed.");
        for (var attempt = 0; attempt <= RetryDelays.Length; attempt++)
        {
            if (attempt > 0)
                await Task.Delay(RetryDelays[attempt - 1], time);

            try
            {
                result = await Task.Run(InstallOrUpdateAsync);
            }
            catch (Exception exception)
            {
                result = exception;
            }

            if (result.IsSuccess)
                break;
            logger.LogWarning(
                result.Error.Exception,
                "Background Retro Rewind install attempt {Attempt} failed: {Message}",
                attempt + 1,
                result.Error.Message
            );
        }

        if (result.IsSuccess)
            Update(BackgroundInstallState.Completed, 100, null, null);
        else
            Update(BackgroundInstallState.Failed, Percent, null, result.Error.Message);
        return result;
    }

    private Task<OperationResult> InstallOrUpdateAsync()
    {
        var progress = new InlineProgress(value =>
        {
            if (value.Message is null && value.Percent is null)
                return;
            Update(BackgroundInstallState.Running, value.Percent ?? Percent, value.Message ?? Message, null);
        });
        var operation = new DistributionOperation(progress, Unattended: true);
        var retroRewind = distributions.RetroRewind;
        // UpdateAsync installs when nothing is there yet, and is a no-op when already current.
        return retroRewind.UpdateAsync(operation);
    }

    private void Update(BackgroundInstallState state, int percent, string? message, string? error)
    {
        State = state;
        Percent = Math.Clamp(percent, 0, 100);
        Message = message;
        ErrorMessage = error;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reports synchronously; <see cref="Progress{T}"/> would post to whatever context started the run.</summary>
    private sealed class InlineProgress(Action<DistributionProgress> report) : IProgress<DistributionProgress>
    {
        public void Report(DistributionProgress value) => report(value);
    }
}
