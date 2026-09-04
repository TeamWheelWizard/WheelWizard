using Microsoft.Extensions.Logging.Abstractions;
using WheelWizard.Recomp;

namespace WheelWizard.Test.Features.Recomp;

public class RecompProcessRunnerTests
{
    [Fact]
    public async Task Cancellation_OnLinux_LetsTheHostExitOnSigterm()
    {
        if (OperatingSystem.IsWindows())
            return;

        var runner = new RecompProcessRunner(NullLogger<RecompProcessRunner>.Instance);
        var lines = new List<string>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // The Linux host exits 130 after rolling back on SIGTERM; a shell trap stands in for it here.
        var result = await runner.RunAsync(
            "bash",
            "-c \"trap 'echo cancelled; exit 130' TERM; sleep 30 & wait\"",
            workingDirectory: null,
            lines.Add,
            cancellation.Token
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(130, result.Value);
        Assert.Contains("cancelled", lines);
    }
}
