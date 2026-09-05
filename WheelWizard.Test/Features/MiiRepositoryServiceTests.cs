using Testably.Abstractions.Testing;
using WheelWizard.Recomp;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Test.Features.Settings;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Test.Features;

[Collection("SettingsFeature")]
public sealed class MiiRepositoryServiceTests : IDisposable
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly ISettingsManager _settings;
    private readonly RecompDolphinDataService _dolphinData;
    private readonly MiiRepositoryServiceService _repository;
    private readonly string _sourceNand = Path.GetFullPath("MiiTests/Dolphin/Wii");
    private bool _recompEnabled;
    private bool _copyEnabled;
    private bool _useDolphinData = true;

    public MiiRepositoryServiceTests()
    {
        _settings = SettingsTestUtils.InitializeSettingsRuntime(Path.GetDirectoryName(_sourceNand)!);
        var nandSetting = new WhWzSetting(typeof(string), "NandRoot", _sourceNand);
        var copySetting = new WhWzSetting(typeof(bool), "CopyNand", false);
        var useSetting = new WhWzSetting(typeof(bool), "UseDolphinData", true);
        _settings.NAND_ROOT_PATH.Returns(nandSetting);
        _settings.RECOMP_COPY_DOLPHIN_NAND.Returns(copySetting);
        _settings.RECOMP_USE_DOLPHIN_DATA.Returns(useSetting);
        _settings.Get<string>(nandSetting).Returns(_sourceNand);
        _settings.Get<bool>(copySetting).Returns(_ => _copyEnabled);
        _settings.Get<bool>(useSetting).Returns(_ => _useDolphinData);
        _settings.IsRecompModeActive().Returns(_ => _recompEnabled);
        _fileSystem.Directory.CreateDirectory(_sourceNand);
        _dolphinData = new RecompDolphinDataService(_settings, Substitute.For<IRecompSettingManager>(), _fileSystem);
        _repository = new MiiRepositoryServiceService(_fileSystem, _settings, _dolphinData);
    }

    [Fact]
    public void CopiedNand_ReadsAndEditsCopy_AndSwitchesBackWithoutRestart()
    {
        Assert.True(_repository.ForceCreateDatabase().IsSuccess);
        Assert.True(_repository.AddMiiToBlocks(Block(1)).IsSuccess);
        var sourceBytes = _fileSystem.File.ReadAllBytes(DbPath(_sourceNand));
        Assert.True(_dolphinData.CopyNandForRecomp().IsSuccess);
        _copyEnabled = true;
        _recompEnabled = true;

        Assert.NotNull(_repository.GetRawBlockByAvatarId(1));
        Assert.True(_repository.UpdateBlockByClientId(1, Block(2)).IsSuccess);
        Assert.Null(_repository.GetRawBlockByAvatarId(1));
        Assert.NotNull(_repository.GetRawBlockByAvatarId(2));
        Assert.Equal(sourceBytes, _fileSystem.File.ReadAllBytes(DbPath(_sourceNand)));

        _copyEnabled = false;
        Assert.NotNull(_repository.GetRawBlockByAvatarId(1));
        Assert.Null(_repository.GetRawBlockByAvatarId(2));

        _copyEnabled = true;
        _recompEnabled = false;
        Assert.NotNull(_repository.GetRawBlockByAvatarId(1));
        Assert.Null(_repository.GetRawBlockByAvatarId(2));
    }

    [Fact]
    public void DirectNand_UsesCustomDolphinNand_ForCreationAndEdits()
    {
        _recompEnabled = true;
        Assert.False(_repository.Exists());
        Assert.True(_repository.ForceCreateDatabase().IsSuccess);
        Assert.True(_repository.AddMiiToBlocks(Block(3)).IsSuccess);
        Assert.True(_fileSystem.File.Exists(DbPath(_sourceNand)));

        _recompEnabled = false;
        Assert.NotNull(_repository.GetRawBlockByAvatarId(3));
    }

    [Fact]
    public void UpdateBlockByClientId_UsesOneResolvedPathForReadAndWrite()
    {
        Assert.True(_repository.ForceCreateDatabase().IsSuccess);
        Assert.True(_repository.AddMiiToBlocks(Block(1)).IsSuccess);

        _recompEnabled = true;
        var alternateNand = Path.GetFullPath("MiiTests/Alternate/Wii");
        var alternateData = Substitute.For<IRecompDolphinDataService>();
        alternateData.NandFolderPath.Returns(alternateNand);
        var alternateRepository = new MiiRepositoryServiceService(_fileSystem, _settings, alternateData);
        Assert.True(alternateRepository.ForceCreateDatabase().IsSuccess);
        Assert.True(alternateRepository.AddMiiToBlocks(Block(2)).IsSuccess);

        // If the repository resolves the property again while loading, it will see the alternate
        // NAND and miss Mii 1 before it gets a chance to write anything.
        var changingData = Substitute.For<IRecompDolphinDataService>();
        changingData.NandFolderPath.Returns(_sourceNand, alternateNand, _sourceNand);
        var repository = new MiiRepositoryServiceService(_fileSystem, _settings, changingData);

        Assert.True(repository.UpdateBlockByClientId(1, Block(3)).IsSuccess);
        Assert.Equal(3, _fileSystem.File.ReadAllBytes(DbPath(_sourceNand))[0x04 + 0x1B]);
        Assert.Equal(2, _fileSystem.File.ReadAllBytes(DbPath(alternateNand))[0x04 + 0x1B]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnavailableLinkedNand_CreatesPrivateDatabase_WithoutTouchingDolphin(bool missingCopy)
    {
        Assert.True(_repository.ForceCreateDatabase().IsSuccess);
        Assert.True(_repository.AddMiiToBlocks(Block(4)).IsSuccess);
        var sourceBytes = _fileSystem.File.ReadAllBytes(DbPath(_sourceNand));
        _recompEnabled = true;
        _copyEnabled = missingCopy;
        _useDolphinData = missingCopy;

        Assert.False(_repository.Exists());
        Assert.Empty(_repository.LoadAllBlocks());
        Assert.True(_repository.ForceCreateDatabase().IsSuccess);
        Assert.True(_repository.AddMiiToBlocks(Block(5)).IsSuccess);
        Assert.NotNull(_repository.GetRawBlockByAvatarId(5));
        Assert.Null(_repository.GetRawBlockByAvatarId(4));
        Assert.True(_fileSystem.File.Exists(DbPath(Path.Combine(PathManager.RecompUserDataFolderPath, "NAND"))));
        Assert.Equal(sourceBytes, _fileSystem.File.ReadAllBytes(DbPath(_sourceNand)));
    }

    [Fact]
    public void DolphinMode_DoesNotRequireRecompServices()
    {
        var repository = new MiiRepositoryServiceService(_fileSystem, _settings);
        Assert.True(repository.ForceCreateDatabase().IsSuccess);
        Assert.True(_fileSystem.File.Exists(DbPath(_sourceNand)));
    }

    private static byte[] Block(byte id)
    {
        var block = new byte[74];
        block[0x1B] = id;
        return block;
    }

    private static string DbPath(string nand) => Path.Combine(nand, "shared2", "menu", "FaceLib", "RFL_DB.dat");

    public void Dispose() => SettingsTestUtils.ResetSettingsRuntime();
}
