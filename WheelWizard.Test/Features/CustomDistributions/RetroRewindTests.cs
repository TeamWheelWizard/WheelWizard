using System.Linq.Expressions;
using Microsoft.Extensions.Logging;
using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using WheelWizard.CustomDistributions.Domain;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Shared;
using WheelWizard.Shared.Services;

namespace WheelWizard.Test.Features.CustomDistributions;

[Collection("SettingsFeature")]
public class RetroRewindTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly IApiCaller<IRetroRewindApi> _api = Substitute.For<IApiCaller<IRetroRewindApi>>();
    private readonly RetroRewind _distribution;

    public RetroRewindTests()
    {
        InitializeUserFolder();
        _distribution = new RetroRewind(_fileSystem, _api, Substitute.For<ILogger<IDistribution>>(), Substitute.For<ISettingsManager>());
    }

    [Fact]
    public async Task ReinstallAsync_KeepsInstallAndPatches_WhenTheServerIsUnreachable()
    {
        CreateExistingInstall();
        ServerIsUnreachable();

        var result = await _distribution.ReinstallAsync(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("6.0.0", _fileSystem.File.ReadAllText(VersionFilePath));
        Assert.Equal("patch", _fileSystem.File.ReadAllText(PatchFilePath));
        Assert.True(_fileSystem.File.Exists(DiscXmlFilePath));
        Assert.False(_fileSystem.Directory.Exists(BackupPath));
    }

    [Fact]
    public async Task ReinstallAsync_ReplacesTheBackupOfAPreviousInterruptedInstall()
    {
        CreateExistingInstall();
        _fileSystem.Directory.CreateDirectory(BackupPath);
        var leftoverFilePath = _fileSystem.Path.Combine(BackupPath, "leftover.txt");
        _fileSystem.File.WriteAllText(leftoverFilePath, "leftover");
        ServerIsUnreachable();

        var result = await _distribution.ReinstallAsync(null!);

        Assert.True(result.IsFailure);
        Assert.Equal("patch", _fileSystem.File.ReadAllText(PatchFilePath));
        Assert.False(_fileSystem.File.Exists(leftoverFilePath));
        Assert.False(_fileSystem.Directory.Exists(BackupPath));
    }

    [Fact]
    public async Task RemoveAsync_DeletesTheInstallAndItsDiscXmlFile()
    {
        CreateExistingInstall();

        var result = await _distribution.RemoveAsync(null!);

        Assert.True(result.IsSuccess);
        Assert.False(_fileSystem.Directory.Exists(InstallPath));
        Assert.False(_fileSystem.File.Exists(DiscXmlFilePath));
    }

    [Fact]
    public async Task RemoveAsync_ReturnsFailure_WhenTheInstallCannotBeDeleted()
    {
        CreateExistingInstall();
        _fileSystem.Intercept.Event(_ => throw new IOException("The install is in use."));

        var result = await _distribution.RemoveAsync(null!);

        Assert.True(result.IsFailure);
        Assert.Contains("Failed to remove", result.Error.Message);
        Assert.True(_fileSystem.File.Exists(VersionFilePath));
    }

    private static string InstallPath => Path.Combine(PathManager.RiivolutionWhWzFolderPath, "RetroRewind6");
    private static string BackupPath => Path.Combine(PathManager.RiivolutionWhWzFolderPath, "RetroRewind6.old");
    private static string VersionFilePath => Path.Combine(InstallPath, "version.txt");
    private static string PatchFilePath => Path.Combine(PathManager.PatchesFolderPath, "MyPatch.szs");
    private static string DiscXmlFilePath => Path.Combine(PathManager.RiivolutionXmlFolderPath, "RetroRewind6.xml");

    private void InitializeUserFolder()
    {
        var settingsManager = new SettingsManager(
            Substitute.For<IWhWzSettingManager>(),
            Substitute.For<IDolphinSettingManager>(),
            _fileSystem
        );
#pragma warning disable CS0618
        SettingsRuntime.Initialize(settingsManager);
#pragma warning restore CS0618

        var userFolderPath = $"/wheelwizard-user-{Guid.NewGuid():N}";
        var loadFolderPath = _fileSystem.Path.Combine(userFolderPath, "Load");
        _fileSystem.Directory.CreateDirectory(loadFolderPath);
        Assert.True(settingsManager.Set(settingsManager.USER_FOLDER_PATH, userFolderPath, skipSave: true));
        Assert.True(settingsManager.Set(settingsManager.LOAD_PATH, loadFolderPath, skipSave: true));
    }

    private void CreateExistingInstall()
    {
        _fileSystem.Directory.CreateDirectory(PathManager.PatchesFolderPath);
        _fileSystem.Directory.CreateDirectory(PathManager.RiivolutionXmlFolderPath);
        _fileSystem.File.WriteAllText(VersionFilePath, "6.0.0");
        _fileSystem.File.WriteAllText(PatchFilePath, "patch");
        _fileSystem.File.WriteAllText(DiscXmlFilePath, "<wiidisc/>");
    }

    private void ServerIsUnreachable()
    {
        _api.CallApiAsync(Arg.Any<Expression<Func<IRetroRewindApi, Task<string>>>>())
            .Returns(Task.FromResult<OperationResult<string>>(Fail("Server offline")));
    }
}
