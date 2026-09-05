using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace WheelWizard.Recomp;

/// <summary>
/// Runs the recomp setup executable. Split out from <see cref="RecompInstallService"/> so the install
/// orchestration can be unit tested without spawning processes.
/// </summary>
public interface IRecompProcessRunner
{
    /// <summary>
    /// Runs a process to completion, forwarding every stdout line to <paramref name="onStandardOutputLine"/>.
    /// Stderr is captured for diagnostics only, since the contract keeps it out of the NDJSON stream.
    /// </summary>
    Task<OperationResult<int>> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        Action<string>? onStandardOutputLine,
        CancellationToken cancellationToken = default
    );
}

/// <inheritdoc />
public sealed class RecompProcessRunner(ILogger<RecompProcessRunner> logger) : IRecompProcessRunner
{
    private const string CancellationEventEnvironmentVariable = "MKWCOMPILED_CANCEL_EVENT";

    // The AppImage runtime honours this by unpacking itself to a temporary directory instead of
    // mounting through FUSE, which is what makes it run on machines without libfuse2.
    private const string AppImageExtractAndRunEnvironmentVariable = "APPIMAGE_EXTRACT_AND_RUN";

    private static readonly TimeSpan CancellationGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ForcedExitGracePeriod = TimeSpan.FromSeconds(5);

    // Once FUSE has failed on this machine it will keep failing, so every later run goes straight to
    // extraction instead of paying for a failed attempt first.
    private static volatile bool _appImageNeedsExtraction;

    public async Task<OperationResult<int>> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        Action<string>? onStandardOutputLine,
        CancellationToken cancellationToken = default
    )
    {
        var isAppImage = !OperatingSystem.IsWindows() && fileName.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase);
        var extractAndRun = isAppImage && _appImageNeedsExtraction;

        var result = await RunOnceAsync(fileName, arguments, workingDirectory, onStandardOutputLine, extractAndRun, cancellationToken);

        // A FUSE failure happens before the AppImage's payload gets to run, so nothing has been done yet
        // and the same command can simply be retried in extraction mode.
        if (isAppImage && !extractAndRun && result.FailedToMount)
        {
            logger.LogInformation("The WiiCompiled AppImage could not mount through FUSE; running it extracted instead");
            _appImageNeedsExtraction = true;
            result = await RunOnceAsync(fileName, arguments, workingDirectory, onStandardOutputLine, extractAndRun: true, cancellationToken);
        }

        return result.Outcome;
    }

    private async Task<RunOutcome> RunOnceAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        Action<string>? onStandardOutputLine,
        bool extractAndRun,
        CancellationToken cancellationToken
    )
    {
        var sawStandardOutput = false;
        var sawFuseError = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cancellationEventName = $@"Local\MKWCompiled.WheelWizard.Cancel.{Guid.NewGuid():N}";
            // Named wait handles only exist on Windows; elsewhere the constructor throws.
            using var cancellationEvent =
                OperatingSystem.IsWindows() && cancellationToken.CanBeCanceled
                    ? new EventWaitHandle(initialState: false, EventResetMode.ManualReset, cancellationEventName)
                    : null;
            var startInfo = CreateStartInfo(fileName, arguments, workingDirectory);
            if (cancellationEvent is not null)
                startInfo.Environment[CancellationEventEnvironmentVariable] = cancellationEventName;
            if (extractAndRun)
                startInfo.Environment[AppImageExtractAndRunEnvironmentVariable] = "1";

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                    return;
                sawStandardOutput = true;
                onStandardOutputLine?.Invoke(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (string.IsNullOrWhiteSpace(eventArgs.Data))
                    return;
                if (eventArgs.Data.Contains("fuse", StringComparison.OrdinalIgnoreCase))
                    sawFuseError = true;
                logger.LogDebug("Recomp setup stderr: {Line}", eventArgs.Data);
            };

            if (!process.Start())
                return new(Fail($"Failed to start '{fileName}'."), FailedToMount: false);

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
                // WaitForExitAsync observes the process handle. The synchronous wait additionally guarantees
                // that both asynchronous redirected-output readers have delivered their final lines.
                process.WaitForExit();
            }
            catch (OperationCanceledException)
            {
                var exited = cancellationEvent is not null
                    ? await CancelAndWaitForExitAsync(process, cancellationEvent)
                    : OperatingSystem.IsWindows()
                        ? await KillAndWaitForExitAsync(process)
                        : await TerminateAndWaitForExitAsync(process);
                if (!exited)
                    throw;

                // Cooperative cancellation is a request, not proof that the backend abandoned its
                // transaction. Once it exits, the drained terminal output and actual exit code say
                // whether cancellation won before commit (failure) or commit won the race (success).
                return new(Ok(process.ExitCode), FailedToMount: false);
            }

            // The AppImage runtime exits non-zero without ever reaching its payload when FUSE is missing;
            // any stdout at all proves the payload ran and the failure is its own.
            var failedToMount = process.ExitCode != 0 && !sawStandardOutput && sawFuseError;
            return new(Ok(process.ExitCode), failedToMount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to run '{FileName}'", fileName);
            return new(Fail(exception), FailedToMount: false);
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string arguments, string? workingDirectory) =>
        new()
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? string.Empty : workingDirectory,
            UseShellExecute = false,
            // The recomp setup is CLI-only. Wheel Wizard supplies the UI, including during launch,
            // so the helper process must never flash a console window behind the game.
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

    private async Task<bool> CancelAndWaitForExitAsync(Process process, EventWaitHandle cancellationEvent)
    {
        try
        {
            cancellationEvent.Set();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to signal cooperative cancellation to the recomp setup process");
        }

        return await WaitForCooperativeExitAsync(process);
    }

    /// <summary>
    /// The Unix equivalent of the named event: the AppImage's setup handles SIGTERM by stopping the
    /// build it spawned and writing its terminal result line, so it gets the same grace period.
    /// </summary>
    private async Task<bool> TerminateAndWaitForExitAsync(Process process)
    {
        try
        {
            if (!process.HasExited && Kill(process.Id, Sigterm) != 0)
                logger.LogWarning("Failed to send SIGTERM to the recomp setup process (errno {Errno})", Marshal.GetLastPInvokeError());
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to signal cooperative cancellation to the recomp setup process");
        }

        return await WaitForCooperativeExitAsync(process);
    }

    private async Task<bool> WaitForCooperativeExitAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync().WaitAsync(CancellationGracePeriod);
            process.WaitForExit();
            return true;
        }
        catch (TimeoutException)
        {
            logger.LogWarning(
                "Recomp setup did not exit within {GracePeriodSeconds} seconds after cooperative cancellation; stopping it",
                CancellationGracePeriod.TotalSeconds
            );
        }

        return await KillAndWaitForExitAsync(process);
    }

    private async Task<bool> KillAndWaitForExitAsync(Process process)
    {
        if (!TryKill(process) && !process.HasExited)
            return false;

        try
        {
            await process.WaitForExitAsync().WaitAsync(ForcedExitGracePeriod);
            process.WaitForExit();
            return true;
        }
        catch (TimeoutException)
        {
            logger.LogWarning("Recomp setup did not exit after its process tree was stopped");
            if (!process.HasExited)
                return false;

            process.WaitForExit();
            return true;
        }
    }

    private bool TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to stop the recomp setup process after cancellation");
            return false;
        }
    }

    private const int Sigterm = 15;

    // .NET offers no way to send a specific signal to another process, so this goes to libc directly.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);

    private sealed record RunOutcome(OperationResult<int> Outcome, bool FailedToMount);
}
