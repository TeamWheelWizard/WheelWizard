using System.Globalization;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Testably.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.DolphinInstaller;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.Settings;

[CollectionDefinition("SettingsFeature", DisableParallelization = true)]
public sealed class SettingsFeatureCollection;

[Collection("SettingsFeature")]
public class SettingsManagerTests
{
    [Fact]
    public void Get_Throws_WhenRequestedTypeDoesNotMatchSettingType()
    {
        var manager = CreateManager(new MockFileSystem(), out _, out _, out _);

        Assert.Throws<InvalidOperationException>(() => manager.Get<int>(manager.WW_LANGUAGE));
    }

    [Fact]
    public void Set_Throws_WhenProvidedValueIsNull()
    {
        var manager = CreateManager(new MockFileSystem(), out _, out _, out _);

        Assert.Throws<ArgumentNullException>(() => manager.Set<string>(manager.WW_LANGUAGE, null!));
    }

    [Fact]
    public void Set_ReturnsFalse_WhenValidationFails()
    {
        var manager = CreateManager(new MockFileSystem(), out _, out _, out _);

        var result = manager.Set(manager.FOCUSED_USER, 99, skipSave: true);

        Assert.False(result);
        Assert.Equal(0, manager.Get<int>(manager.FOCUSED_USER));
    }

    [Fact]
    public void ValidateCorePathSettings_ReturnsAllExpectedIssues_WhenDefaultsAreInvalid()
    {
        var manager = CreateManager(new RealFileSystem(), out _, out _, out _);
#pragma warning disable CS0618
        SettingsRuntime.Initialize(manager);
#pragma warning restore CS0618

        var result = manager.ValidateCorePathSettings();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsValid);
        Assert.Contains(result.Value.Issues, issue => issue.Code == SettingsValidationCode.InvalidUserFolderPath);
        Assert.Contains(result.Value.Issues, issue => issue.Code == SettingsValidationCode.InvalidDolphinLocation);
        Assert.Contains(result.Value.Issues, issue => issue.Code == SettingsValidationCode.InvalidGameLocation);
    }

    [Fact]
    public void PathsSetupCorrectly_ReturnsTrue_WhenCorePathsAreValid()
    {
        var fileSystem = new MockFileSystem();
        var manager = CreateManager(fileSystem, out _, out _, out _);
        var userFolderPath = $"/wheelwizard-user-{Guid.NewGuid():N}";
        var gameFilePath = Path.Combine(userFolderPath, "game.iso");
        var dolphinLocation = SettingsTestUtils.GetValidDolphinLocation(fileSystem);
        fileSystem.Directory.CreateDirectory(userFolderPath);
        fileSystem.File.WriteAllText(gameFilePath, "iso");
#pragma warning disable CS0618
        SettingsRuntime.Initialize(manager);
#pragma warning restore CS0618

        Assert.True(manager.Set(manager.USER_FOLDER_PATH, userFolderPath, skipSave: true));
        Assert.True(manager.Set(manager.GAME_LOCATION, gameFilePath, skipSave: true));
        Assert.True(manager.Set(manager.DOLPHIN_LOCATION, dolphinLocation, skipSave: true));
        Assert.True(manager.PathsSetupCorrectly());
    }

    [Fact]
    public void LoadSettings_CallsUnderlyingManagersOnlyOnce()
    {
        var manager = CreateManager(new MockFileSystem(), out var whWzManager, out var dolphinManager, out _);

        manager.LoadSettings();
        manager.LoadSettings();

        whWzManager.Received(1).LoadSettings();
        dolphinManager.Received(1).LoadSettings();
    }

    private static SettingsManager CreateManager(
        IFileSystem fileSystem,
        out IWhWzSettingManager whWzSettingManager,
        out IDolphinSettingManager dolphinSettingManager,
        out ILinuxDolphinInstaller linuxDolphinInstaller
    )
    {
        whWzSettingManager = Substitute.For<IWhWzSettingManager>();
        dolphinSettingManager = Substitute.For<IDolphinSettingManager>();
        linuxDolphinInstaller = Substitute.For<ILinuxDolphinInstaller>();
        linuxDolphinInstaller.IsDolphinInstalledInFlatpak().Returns(true);

        return new SettingsManager(whWzSettingManager, dolphinSettingManager, linuxDolphinInstaller, fileSystem);
    }
}

[Collection("SettingsFeature")]
public class SettingsSignalBusTests
{
    [Fact]
    public void Publish_NotifiesActiveSubscribers()
    {
        var signalBus = new SettingsSignalBus();
        var setting = new WhWzSetting(typeof(int), "Volume", 10);
        SettingChangedSignal? receivedSignal = null;
        using var _ = signalBus.Subscribe(signal => receivedSignal = signal);

        signalBus.Publish(setting);

        Assert.True(receivedSignal.HasValue);
        Assert.Same(setting, receivedSignal.Value.Setting);
    }

    [Fact]
    public void DisposeSubscription_StopsReceivingSignals()
    {
        var signalBus = new SettingsSignalBus();
        var setting = new WhWzSetting(typeof(int), "Volume", 10);
        var receiveCount = 0;
        var subscription = signalBus.Subscribe(_ => receiveCount++);

        signalBus.Publish(setting);
        subscription.Dispose();
        signalBus.Publish(setting);

        Assert.Equal(1, receiveCount);
    }

    [Fact]
    public void Subscribe_Throws_WhenHandlerIsNull()
    {
        var signalBus = new SettingsSignalBus();

        Assert.Throws<ArgumentNullException>(() => signalBus.Subscribe(null!));
    }
}

[Collection("SettingsFeature")]
public class SettingsLocalizationServiceTests
{
    [Fact]
    public void Initialize_SetsCurrentCulture_FromLanguageSetting()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var signalBus = new SettingsSignalBus();
        var settingsManager = Substitute.For<ISettingsManager>();
        var languageSetting = new WhWzSetting(typeof(string), "WW_Language", "fr");
        settingsManager.WW_LANGUAGE.Returns(languageSetting);
        settingsManager.Get<string>(Arg.Any<Setting>()).Returns(_ => (string)languageSetting.Get());
        var localizationService = new SettingsLocalizationService(settingsManager, signalBus);

        try
        {
            localizationService.Initialize();

            Assert.Equal("fr", CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            Assert.Equal("fr", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void PublishLanguageSignal_UpdatesCulture_WhenLanguageChanges()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var signalBus = new SettingsSignalBus();
        var settingsManager = Substitute.For<ISettingsManager>();
        var languageSetting = new WhWzSetting(typeof(string), "WW_Language", "en");
        settingsManager.WW_LANGUAGE.Returns(languageSetting);
        settingsManager.Get<string>(Arg.Any<Setting>()).Returns(_ => (string)languageSetting.Get());
        var localizationService = new SettingsLocalizationService(settingsManager, signalBus);

        try
        {
            localizationService.Initialize();
            languageSetting.Set("de", skipSave: true);
            signalBus.Publish(languageSetting);

            Assert.Equal("de", CultureInfo.CurrentCulture.TwoLetterISOLanguageName);
            Assert.Equal("de", CultureInfo.CurrentUICulture.TwoLetterISOLanguageName);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}

[Collection("SettingsFeature")]
public class SettingsStartupInitializerTests
{
    [Fact]
    public void Initialize_LoadsSettings_InitializesLocalization_AndSetsRuntimes()
    {
        var settingsManager = Substitute.For<ISettingsManager>();
        var signalBus = new SettingsSignalBus();
        var localizationService = Substitute.For<ISettingsLocalizationService>();
        var logger = Substitute.For<ILogger<SettingsStartupInitializer>>();
        settingsManager.ValidateCorePathSettings().Returns(Ok(new SettingsValidationReport([])));
        var initializer = new SettingsStartupInitializer(settingsManager, signalBus, localizationService, logger);

        initializer.Initialize();

        settingsManager.Received(1).LoadSettings();
        localizationService.Received(1).Initialize();
#pragma warning disable CS0618
        Assert.Same(settingsManager, SettingsRuntime.Current);
#pragma warning restore CS0618
    }

    [Fact]
    public void Initialize_DoesNotThrow_WhenValidationFails()
    {
        var settingsManager = Substitute.For<ISettingsManager>();
        var signalBus = new SettingsSignalBus();
        var localizationService = Substitute.For<ISettingsLocalizationService>();
        var logger = Substitute.For<ILogger<SettingsStartupInitializer>>();
        settingsManager.ValidateCorePathSettings().Returns(Fail("validation failed"));
        var initializer = new SettingsStartupInitializer(settingsManager, signalBus, localizationService, logger);

        var exception = Record.Exception(initializer.Initialize);

        Assert.Null(exception);
        settingsManager.Received(1).LoadSettings();
        localizationService.Received(1).Initialize();
    }
}

internal static class SettingsTestUtils
{
    public static ISettingsManager InitializeSettingsRuntime(string userFolderPath, string dolphinLocation = "dolphin-emu")
    {
        var settings = CreateRuntimeSettingsStub(userFolderPath, dolphinLocation);
#pragma warning disable CS0618
        SettingsRuntime.Initialize(settings);
#pragma warning restore CS0618
        return settings;
    }

    public static void InitializeSignalRuntime(ISettingsSignalBus? signalBus = null)
    {
#pragma warning disable CS0618
        SettingsSignalRuntime.Initialize(signalBus ?? new SettingsSignalBus());
#pragma warning restore CS0618
    }

    public static string GetValidDolphinLocation(IFileSystem fileSystem)
    {
        if (!OperatingSystem.IsWindows())
            return "/usr/bin/env";

        const string exePath = @"C:\WheelWizardTests\Dolphin.exe";
        var directoryPath = fileSystem.Path.GetDirectoryName(exePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
            fileSystem.Directory.CreateDirectory(directoryPath);

        fileSystem.File.WriteAllText(exePath, "test");
        return exePath;
    }

    private static ISettingsManager CreateRuntimeSettingsStub(string userFolderPath, string dolphinLocation)
    {
        var settings = Substitute.For<ISettingsManager>();
        var userFolderSetting = new WhWzSetting(typeof(string), "UserFolderPath", userFolderPath);
        var dolphinLocationSetting = new WhWzSetting(typeof(string), "DolphinLocation", dolphinLocation);

        settings.USER_FOLDER_PATH.Returns(userFolderSetting);
        settings.DOLPHIN_LOCATION.Returns(dolphinLocationSetting);

        settings
            .Get<string>(Arg.Is<Setting>(setting => ReferenceEquals(setting, userFolderSetting)))
            .Returns(_ => (string)userFolderSetting.Get());
        settings
            .Get<string>(Arg.Is<Setting>(setting => ReferenceEquals(setting, dolphinLocationSetting)))
            .Returns(_ => (string)dolphinLocationSetting.Get());

        return settings;
    }
}
