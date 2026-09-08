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
    // mounting through FUSE. It is always set: FUSE is not something to rely on (it is missing on
    // many distributions and inside a Flatpak sandbox), and the unpack costs well under a second.
    private const string AppImageExtractAndRunEnvironmentVariable = "APPIMAGE_EXTRACT_AND_RUN";

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
            if (!OperatingSystem.IsWindows())
                startInfo.Environment[AppImageExtractAndRunEnvironmentVariable] = "1";

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
                    cancellationEvent is not null ? await CancelAndWaitForExitAsync(process, cancellationEvent)
                    : OperatingSystem.IsWindows() ? await KillAndWaitForExitAsync(process)
                    : await TerminateAndWaitForExitAsync(process);
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
    /// The process Wheel Wizard started is the AppImage runtime, which runs the setup in a child and
    /// does not forward signals to it, so every descendant is signalled, not just the root. The list is
    /// taken before signalling: once the runtime dies its children are reparented and no longer found.
    /// </summary>
    private async Task<bool> TerminateAndWaitForExitAsync(Process process)
    {
        var processIds = ProcessTreeIds(process);
        try
        {
            foreach (var processId in processIds)
            {
                if (Kill(processId, Sigterm) != 0)
                    logger.LogDebug(
                        "Failed to send SIGTERM to recomp process {Pid} (errno {Errno})",
                        processId,
                        Marshal.GetLastPInvokeError()
                    );
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to signal cooperative cancellation to the recomp setup process");
        }

        try
        {
            // The redirected streams only close once every descendant holding them has exited, so this
            // waits for the setup's own terminal result line, not merely for the runtime.
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

        foreach (var processId in processIds)
            Kill(processId, Sigkill);
        return await KillAndWaitForExitAsync(process);
    }

    /// <summary>The started process and all of its descendants, from <c>/proc</c>, root first.</summary>
    private List<int> ProcessTreeIds(Process root)
    {
        var childrenByParent = new Dictionary<int, List<int>>();
        try
        {
            foreach (var directory in Directory.EnumerateDirectories("/proc"))
            {
                if (!int.TryParse(Path.GetFileName(directory), out var processId))
                    continue;
                try
                {
                    // "<pid> (<comm>) <state> <ppid> ...": comm may contain spaces, so split after its closing parenthesis.
                    var stat = File.ReadAllText(Path.Combine(directory, "stat"));
                    var fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                    var parentId = int.Parse(fields[1]);
                    if (!childrenByParent.TryGetValue(parentId, out var children))
                        childrenByParent[parentId] = children = [];
                    children.Add(processId);
                }
                catch (Exception)
                {
                    // The process vanished between the listing and the read, or /proc is not readable.
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not enumerate the recomp setup's process tree");
        }

        var result = new List<int> { root.Id };
        for (var index = 0; index < result.Count; index++)
        {
            if (childrenByParent.TryGetValue(result[index], out var children))
                result.AddRange(children);
        }

        return result;
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
    private const int Sigkill = 9;

    // .NET offers no way to send a specific signal to another process, so this goes to libc directly.
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int pid, int signal);
}
