using System.Reflection;
using Testably.Abstractions.Testing;
using WheelWizard.Recomp;
using WheelWizard.Services;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Test.Features.Settings;

namespace WheelWizard.Test.Features.Recomp;

[Collection("SettingsFeature")]
public sealed class RecompDolphinDataServiceTests : IDisposable
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly RecompSettingManager _recompSettings;
    private readonly SettingsManager _settings;
    private readonly RecompDolphinDataService _service;
    private readonly string _userFolder = Path.GetFullPath("RecompPathTests/Dolphin User");

    public RecompDolphinDataServiceTests()
    {
        SettingsTestUtils.ResetSignalRuntime();
        _recompSettings = new RecompSettingManager(_fileSystem);
        _settings = new SettingsManager(
            Substitute.For<IWhWzSettingManager>(),
            Substitute.For<IDolphinSettingManager>(),
            _recompSettings,
            _fileSystem
        );
#pragma warning disable CS0618
        SettingsRuntime.Initialize(_settings);
#pragma warning restore CS0618
        // Testably throws for Directory.Exists(""); the real filesystem returns false.
        foreach (var pathSetting in new[] { _settings.LOAD_PATH, _settings.NAND_ROOT_PATH })
            pathSetting.SetValidation(value => value is string path && path.Length > 0 && _fileSystem.Directory.Exists(path));
        _service = new RecompDolphinDataService(_settings, _recompSettings, _fileSystem);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyPaths_ReplacesStalePack_WithThePackUsedByTheLicenseManager(bool sharing)
    {
        SetUserFolder(_userFolder);
        _service.SetSharingEnabled(sharing);
        WriteConfig(
            """
            # Keep the runtime's other settings.
            [paths]
            dvd_root = '../Install/Game Files'
            retro_rewind_root = '../old/RetroRewind6'
            nand_root = 'old-nand'
            [video]
            resolution_multiplier = 3.0
            """
        );

        ApplyPaths();

        Assert.Equal(Path.GetFullPath(PathManager.RetroRewind6FolderPath), ReadPath(_settings.RECOMP_RETRO_REWIND_ROOT));
        var sdRoot = Path.GetDirectoryName(ReadPath(_settings.RECOMP_RETRO_REWIND_ROOT))!;
        Assert.Equal(Path.GetFullPath(PathManager.SaveFolderPath), Path.Combine(sdRoot, "riivolution", "save", "RetroWFC"));
        Assert.Equal(sharing ? Path.Combine(_userFolder, "Wii") : "", ReadPath(_settings.RECOMP_NAND_ROOT));
        Assert.Contains("dvd_root = '../Install/Game Files'", ReadConfig());
        Assert.Contains("resolution_multiplier = 3.0", ReadConfig());
        Assert.Contains("# Keep the runtime's other settings.", ReadConfig());
    }

    [Fact]
    public void ApplyPaths_FollowsLoadOverride_AndSubsequentFolderChangeWithoutReinstall()
    {
        SetUserFolder(_userFolder);
        WriteConfig("[paths]");
        ApplyPaths();
        var loadFolder = Path.GetFullPath("RecompPathTests/Custom Load");
        _fileSystem.Directory.CreateDirectory(loadFolder);
        Assert.True(_settings.Set(_settings.LOAD_PATH, loadFolder));

        ApplyPaths();
        Assert.Equal(Path.Combine(loadFolder, "Riivolution", "WheelWizard", "RetroRewind6"), ReadPath(_settings.RECOMP_RETRO_REWIND_ROOT));

        // A Load override that disappeared falls back in the same way as PathManager.
        _fileSystem.Directory.Delete(loadFolder);
        var newUser = Path.GetFullPath("RecompPathTests/Moved User");
        SetUserFolder(newUser);
        ApplyPaths();
        Assert.Equal(
            Path.Combine(newUser, "Load", "Riivolution", "WheelWizard", "RetroRewind6"),
            ReadPath(_settings.RECOMP_RETRO_REWIND_ROOT)
        );
    }

    [Fact]
    public void ApplyPaths_WithoutDolphin_UsesWheelWizardPack()
    {
        WriteConfig("[paths]");

        ApplyPaths();

        Assert.Equal(
            Path.GetFullPath(Path.Combine(PathManager.WheelWizardAppdataPath, "RetroRewind", "RetroRewind6")),
            ReadPath(_settings.RECOMP_RETRO_REWIND_ROOT)
        );
        Assert.DoesNotContain("nand_root =", ReadConfig());
    }

    [Fact]
    public void Link_AcceptsRedirectedNandWithoutDefaultWiiFolder_AndUsesItForSharingAndCopying()
    {
        SetUserFolder(_userFolder);
        var nand = Path.GetFullPath("RecompPathTests/External NAND");
        _fileSystem.Directory.CreateDirectory(nand);
        _fileSystem.File.WriteAllText(Path.Combine(nand, "test-save"), "source");
        Assert.True(_settings.Set(_settings.NAND_ROOT_PATH, nand));
        WriteConfig("[paths]");

        Assert.False(_fileSystem.Directory.Exists(Path.Combine(_userFolder, "Wii")));
        Assert.Equal(_userFolder, _service.LinkedUserFolderPath);
        Assert.True(_service.Link(_userFolder).IsSuccess);
        ApplyPaths();
        Assert.Equal(PathManager.WiiFolderPath, ReadPath(_settings.RECOMP_NAND_ROOT));

        Assert.True(_service.CopyNandForRecomp().IsSuccess);
        _service.SetCopyEnabled(true);
        _service.SetSharingEnabled(false);
        ApplyPaths();
        Assert.Equal(PathManager.RecompNandCopyFolderPath, ReadPath(_settings.RECOMP_NAND_ROOT));
        Assert.Equal("source", _fileSystem.File.ReadAllText(Path.Combine(PathManager.RecompNandCopyFolderPath, "test-save")));

        _service.SetSharingEnabled(true);
        ApplyPaths();
        Assert.Equal(nand, ReadPath(_settings.RECOMP_NAND_ROOT));
    }

    [Fact]
    public void ApplyPaths_MissingCopy_RemovesStaleSharedNand()
    {
        SetUserFolder(_userFolder);
        _service.SetSharingEnabled(true);
        WriteConfig("[paths]");
        ApplyPaths();
        _service.SetSharingEnabled(false);
        _service.SetCopyEnabled(true);

        ApplyPaths();

        Assert.Null(_service.NandFolderPath);
        Assert.DoesNotContain("nand_root =", ReadConfig());
        Assert.Equal("", ReadPath(_settings.RECOMP_NAND_ROOT));
        Assert.Equal(Path.GetFullPath(PathManager.RetroRewind6FolderPath), ReadPath(_settings.RECOMP_RETRO_REWIND_ROOT));
    }

    [Fact]
    public void ApplyPaths_ReappliesAfterBackendRecreatesConfig_AndIsIdempotent()
    {
        SetUserFolder(_userFolder);
        _service.SetSharingEnabled(true);
        WriteConfig("[paths]");
        ApplyPaths();
        var expected = ReadConfig();

        WriteConfig("[paths]");
        ApplyPaths();
        Assert.Equal(expected, ReadConfig());
        ApplyPaths();
        Assert.Equal(expected, ReadConfig());
    }

    [Fact]
    public void ApplyPaths_PreservesFilesystemRootAsNand()
    {
        var nandRoot = Path.GetPathRoot(_userFolder)!;
        _fileSystem.Directory.CreateDirectory(nandRoot);
        Assert.True(_settings.Set(_settings.NAND_ROOT_PATH, nandRoot));
        _service.SetSharingEnabled(true);
        WriteConfig("[paths]");

        ApplyPaths();

        Assert.Equal(nandRoot, ReadPath(_settings.RECOMP_NAND_ROOT));
    }

    [Fact]
    public void ApplyPaths_DoesNotCreateConfigBeforeBackendInstall()
    {
        ApplyPaths();
        Assert.False(_fileSystem.File.Exists(PathManager.RecompConfigFilePath));
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("shared")]
    [InlineData("private")]
    public void ApplyPaths_AfterAppdataRelocation_UpdatesConfigAtItsCurrentLocation(string mode)
    {
        // Change only the in-memory path: this test must never move real user data or
        // persist an appdata override in the registry or the user's home directory.
        var overrideField = typeof(PathManager).GetField("_wheelWizardAppdataOverride", BindingFlags.NonPublic | BindingFlags.Static)!;
        var previousOverride = overrideField.GetValue(null);
        var oldRoot = Path.GetFullPath("RecompPathTests/Old CT-MKWII");
        var newRoot = Path.GetFullPath("RecompPathTests/Moved CT-MKWII");
        try
        {
            overrideField.SetValue(null, oldRoot);
            var externalNand = Path.GetFullPath("RecompPathTests/External NAND");
            _fileSystem.Directory.CreateDirectory(externalNand);
            Assert.True(_settings.Set(_settings.NAND_ROOT_PATH, externalNand));
            _fileSystem.Directory.CreateDirectory(PathManager.RecompNandCopyFolderPath);
            _fileSystem.File.WriteAllText(Path.Combine(PathManager.RecompNandCopyFolderPath, "test-save"), "copied save");
            _service.SetSharingEnabled(mode == "shared");
            _service.SetCopyEnabled(mode == "copy");
            WriteConfig("[paths]");
            ApplyPaths();
            var oldConfigPath = PathManager.RecompConfigFilePath;

            _fileSystem.Directory.Move(oldRoot, newRoot);
            overrideField.SetValue(null, newRoot);
            ApplyPaths();

            var expectedNand = mode switch
            {
                "copy" => Path.Combine(newRoot, "Recomp", "Nand"),
                "shared" => externalNand,
                _ => "",
            };
            Assert.Equal(expectedNand, ReadPath(_settings.RECOMP_NAND_ROOT));
            Assert.Equal(Path.Combine(newRoot, "RetroRewind", "RetroRewind6"), ReadPath(_settings.RECOMP_RETRO_REWIND_ROOT));
            Assert.Equal("copied save", _fileSystem.File.ReadAllText(Path.Combine(PathManager.RecompNandCopyFolderPath, "test-save")));
            Assert.DoesNotContain("Old CT-MKWII", ReadConfig());
            if (!RecompPlatform.IsLinux)
                Assert.False(_fileSystem.File.Exists(oldConfigPath));
        }
        finally
        {
            overrideField.SetValue(null, previousOverride);
        }
    }

    private void ApplyPaths()
    {
        var result = _service.ApplyPathsToRecompConfig();
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    private void SetUserFolder(string path)
    {
        _fileSystem.Directory.CreateDirectory(path);
        Assert.True(_settings.Set(_settings.USER_FOLDER_PATH, path));
    }

    private void WriteConfig(string text)
    {
        _fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(PathManager.RecompConfigFilePath)!);
        _fileSystem.File.WriteAllText(PathManager.RecompConfigFilePath, text);
    }

    private string ReadConfig() => _fileSystem.File.ReadAllText(PathManager.RecompConfigFilePath);

    private string ReadPath(Setting setting)
    {
        // Read the serialized value through a fresh manager, not the service's cached setting.
        var reader = new RecompSettingManager(_fileSystem);
        var stored = new RecompSetting(typeof(string), ("paths", setting.Name), "", reader.SaveSettings);
        reader.RegisterSetting(stored);
        reader.LoadSettings();
        return Assert.IsType<string>(stored.Get());
    }

    public void Dispose()
    {
        SettingsTestUtils.ResetSettingsRuntime();
        SettingsTestUtils.ResetSignalRuntime();
    }
}
