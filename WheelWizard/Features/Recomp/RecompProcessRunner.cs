using System.Diagnostics;
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
    private static readonly TimeSpan CancellationGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ForcedExitGracePeriod = TimeSpan.FromSeconds(5);

    public async Task<OperationResult<int>> RunAsync(
        string fileName,
        string arguments,
        string? workingDirectory,
        Action<string>? onStandardOutputLine,
        CancellationToken cancellationToken = default
    )
    {
        var cancellationFile =
            OperatingSystem.IsMacOS() && cancellationToken.CanBeCanceled
                ? Path.Combine(Path.GetTempPath(), "mkwc-cancel-" + Guid.NewGuid().ToString("N"))
                : null;
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

            if (cancellationFile is not null)
                startInfo.Environment["MKWCOMPILED_CANCEL_FILE"] = cancellationFile;

            using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

            process.OutputDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is not null)
                    onStandardOutputLine?.Invoke(eventArgs.Data);
            };
            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (!string.IsNullOrWhiteSpace(eventArgs.Data))
                    logger.LogDebug("Recomp setup stderr: {Line}", eventArgs.Data);
            };

            if (!process.Start())
                return Fail($"Failed to start '{fileName}'.");

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
                var exited =
                    cancellationEvent is not null || cancellationFile is not null
                        ? await CancelAndWaitForExitAsync(process, cancellationEvent, cancellationFile)
                        : await KillAndWaitForExitAsync(process);
                if (!exited)
                    throw;

                // Cooperative cancellation is a request, not proof that the backend abandoned its
                // transaction. Once it exits, the drained terminal output and actual exit code say
                // whether cancellation won before commit (failure) or commit won the race (success).
                return Ok(process.ExitCode);
            }

            return Ok(process.ExitCode);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to run '{FileName}'", fileName);
            return Fail(exception);
        }
        finally
        {
            if (cancellationFile is not null)
            {
                try
                {
                    File.Delete(cancellationFile);
                }
                catch (IOException exception)
                {
                    logger.LogDebug(exception, "Could not remove cancellation signal");
                }
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fileName, string arguments, string? workingDirectory) =>
        new()
        {
            FileName = OperatingSystem.IsMacOS() && fileName.EndsWith(".run", StringComparison.Ordinal) ? "/bin/bash" : fileName,
            Arguments =
                OperatingSystem.IsMacOS() && fileName.EndsWith(".run", StringComparison.Ordinal)
                    ? RecompSetupCommandBuilder.Quote(fileName) + " " + arguments
                    : arguments,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? string.Empty : workingDirectory,
            UseShellExecute = false,
            // The recomp setup is CLI-only. Wheel Wizard supplies the UI, including during launch,
            // so the helper process must never flash a console window behind the game.
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

    private async Task<bool> CancelAndWaitForExitAsync(Process process, EventWaitHandle? cancellationEvent, string? cancellationFile)
    {
        try
        {
            cancellationEvent?.Set();
            if (cancellationFile is not null)
                await File.WriteAllTextAsync(cancellationFile, "cancel");
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to signal cooperative cancellation to the recomp setup process");
        }

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
}
