using WheelWizard.Models.Enums;
using WheelWizard.Recomp;
using WheelWizard.Recomp.Domain;

namespace WheelWizard.Test.Features.Recomp;

/// <summary>
/// A deliberately small smoke suite over the three contracts that break silently when the recomp is
/// updated: the command line we hand the setup executable, the report we read back, and the status
/// that report maps onto. Everything here is string in / value out, so it runs the same everywhere.
/// </summary>
public class RecompTests
{
    [Fact]
    public void SilentInstall_BuildsTheCommandLineTheSetupExecutableExpects()
    {
        var arguments = RecompSetupCommandBuilder.BuildSilentInstallArguments(
            new()
            {
                GameFilePath = @"D:\Games\Mario Kart Wii.rvz",
                InstallFolderPath = @"D:\WheelWizard\Recomp\Install",
                RetroRewindFolderPath = @"D:\WheelWizard\RetroRewind6",
                Portable = true,
            }
        );

        Assert.Equal(
            "--silent --game \"D:\\Games\\Mario Kart Wii.rvz\" --install-dir \"D:\\WheelWizard\\Recomp\\Install\" --portable "
                + "--progress-json --retro-dir \"D:\\WheelWizard\\RetroRewind6\" --download-retro-wfc-payload",
            arguments
        );
    }

    [Fact]
    public void SilentInstall_SkipsThePayloadOnlyWhenAskedTo()
    {
        var arguments = RecompSetupCommandBuilder.BuildSilentInstallArguments(
            new()
            {
                GameFilePath = @"D:\Games\Mario Kart Wii.rvz",
                InstallFolderPath = @"D:\WheelWizard\Recomp\Install",
                RetroRewindFolderPath = @"D:\WheelWizard\RetroRewind6",
                RetroWfcPayloadMode = RecompRetroWfcPayloadMode.Skip,
            }
        );

        Assert.EndsWith("--retro-dir \"D:\\WheelWizard\\RetroRewind6\" --skip-retro-wfc-payload", arguments);
        Assert.DoesNotContain("--download-retro-wfc-payload", arguments);
    }

    [Fact]
    public void RepairProducts_PassesExactlyOnePayloadOption()
    {
        Assert.Equal(
            "--repair-products --install-dir \"D:\\Recomp\" --retro-dir \"D:\\RetroRewind6\" --download-retro-wfc-payload --progress-json",
            RecompSetupCommandBuilder.BuildRepairProductsArguments(@"D:\Recomp", @"D:\RetroRewind6")
        );
        Assert.Equal(
            "--repair-products --install-dir \"D:\\Recomp\" --retro-dir \"D:\\RetroRewind6\" --skip-retro-wfc-payload --progress-json",
            RecompSetupCommandBuilder.BuildRepairProductsArguments(@"D:\Recomp", @"D:\RetroRewind6", RecompRetroWfcPayloadMode.Skip)
        );
    }

    [Fact]
    public void InstallState_ReadsThePayloadModeTheSetupHostWrites()
    {
        var state = System.Text.Json.JsonSerializer.Deserialize<RecompInstallState>(
            """{"SchemaVersion":1,"SetupVersion":"0.3.0","InstallDir":"D:\\Recomp","RetroWfcPayloadMode":"skipped"}""",
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );

        Assert.NotNull(state);
        Assert.True(state.IsRetroWfcPayloadSkipped);
    }

    [Fact]
    public void ProductsLine_IsReadBackAsTheProductStateItReports()
    {
        var parsed = RecompSetupOutputParser.Parse(
            """
            {"type":"products","setupVersion":"0.3.0","installDir":"D:\\WiiCompiled","rebuildRequired":false,"base":{"status":"current","detail":"ok"},"retroRewind":{"status":"code-pul-changed","detail":"Code.pul changed"}}
            """
        );

        var products = Assert.IsType<RecompProductsEvent>(parsed);
        Assert.Equal("0.3.0", products.SetupVersion);
        Assert.True(products.Base.IsCurrent);
        Assert.Equal(RecompProductState.CodePulChanged, products.RetroRewind.State);
        Assert.True(products.ActionRequired);
    }

    [Fact]
    public void Status_IsOnlyReadyWhenTheInstallWasActuallyVerified()
    {
        var current = new RecompProductStatus(RecompProductState.Current, "ok");
        var verified = new RecompProductsEvent("0.3.0", @"D:\WiiCompiled", RebuildRequired: false, current, current);

        Assert.Equal(WheelWizardStatus.Ready, RecompStatusResolver.Resolve(true, "0.3.0", "v0.3.0", verified));
        Assert.Equal(WheelWizardStatus.OutOfDate, RecompStatusResolver.Resolve(true, "0.3.0", "v0.4.0", verified));
        Assert.Equal(WheelWizardStatus.NotInstalled, RecompStatusResolver.Resolve(true, null, "v0.3.0", verified));
        Assert.Equal(WheelWizardStatus.ConfigNotFinished, RecompStatusResolver.Resolve(false, "0.3.0", "v0.3.0", verified));

        // A check that never answered must never read as ready.
        Assert.Equal(WheelWizardStatus.OutOfDate, RecompStatusResolver.Resolve(true, "0.3.0", "v0.3.0", products: null));
    }
}
