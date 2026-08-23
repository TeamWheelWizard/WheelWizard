using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WheelWizard.DolphinInstaller;

public interface ILinuxProcessService
{
    OperationResult<int> Run(string fileName, string arguments, out string stdOut, out string stdErr);
    OperationResult<int> Run(string fileName, IEnumerable<string> argumentList, out string stdOut, out string stdErr);
    OperationResult<int> Run(string fileName, string arguments);
    Task<OperationResult<int>> RunWithProgressAsync(string fileName, string arguments, IProgress<int>? progress = null);
    Task<OperationResult> LaunchAndStopAsync(string fileName, string arguments, TimeSpan duration);
}

public sealed class LinuxProcessService : ILinuxProcessService
{
    public OperationResult<int> Run(string fileName, string arguments, out string stdOut, out string stdErr)
    {
        var processInfo = new ProcessStartInfo { FileName = fileName, Arguments = arguments };
        return Run(processInfo, $"{fileName} {arguments}", out stdOut, out stdErr);
    }

    public OperationResult<int> Run(string fileName, IEnumerable<string> argumentList, out string stdOut, out string stdErr)
    {
        var processInfo = new ProcessStartInfo { FileName = fileName };
        foreach (var argument in argumentList)
            processInfo.ArgumentList.Add(argument);

        return Run(processInfo, $"{fileName} {string.Join(' ', processInfo.ArgumentList)}", out stdOut, out stdErr);
    }

    private static OperationResult<int> Run(ProcessStartInfo processInfo, string commandText, out string stdOut, out string stdErr)
    {
        var localStdOut = "";
        var localStdErr = "";
        var result = TryCatch(
            () =>
            {
                processInfo.RedirectStandardOutput = true;
                processInfo.RedirectStandardError = true;
                processInfo.UseShellExecute = false;
                processInfo.CreateNoWindow = true;

                using var process = Process.Start(processInfo);
                if (process == null)
                    return -1;

                localStdOut = process.StandardOutput.ReadToEnd();
                localStdErr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode;
            },
            $"Failed to run process: {commandText}"
        );

        stdOut = localStdOut;
        stdErr = localStdErr;
        return result;
    }

    public OperationResult<int> Run(string fileName, string arguments)
    {
        return Run(fileName, arguments, out _, out _);
    }

    public async Task<OperationResult<int>> RunWithProgressAsync(string fileName, string arguments, IProgress<int>? progress = null)
    {
        return await TryCatch(
            async () =>
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(processInfo);
                if (process == null)
                    return -1;

                process.OutputDataReceived += (_, eventArgs) => ReportProgress(eventArgs.Data, progress);
                process.ErrorDataReceived += (_, eventArgs) => ReportProgress(eventArgs.Data, progress);

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();
                return process.ExitCode;
            },
            $"Failed to run process: {fileName} {arguments}"
        );
    }

    public async Task<OperationResult> LaunchAndStopAsync(string fileName, string arguments, TimeSpan duration)
    {
        return await TryCatch(
            async () =>
            {
                using var process = new Process
                {
                    StartInfo = new()
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    },
                };

                process.Start();
                await Task.Delay(duration);

                if (!process.HasExited)
                    process.Kill();
            },
            $"Failed to run process: {fileName} {arguments}"
        );
    }

    private static void ReportProgress(string? output, IProgress<int>? progress)
    {
        if (string.IsNullOrWhiteSpace(output))
            return;

        var match = Regex.Match(output, @"(\d+)%");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
            progress?.Report(percent);
    }
}
