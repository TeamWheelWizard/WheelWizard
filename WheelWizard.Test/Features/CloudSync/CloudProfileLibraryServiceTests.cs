using System.Text.Json;
using WheelWizard.CloudSync;
using WheelWizard.CloudSync.ProfileLibrary;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Test.Features.Settings;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features.CloudSync;

[Collection("SettingsFeature")]
public sealed class CloudProfileLibraryServiceTests : IDisposable
{
    [Fact]
    public void GetVisible_WithoutStoredSelection_ShowsAtMostFourLocalOrVaultProfiles()
    {
        var (service, _) = CreateService("[]", "");
        var profiles = Profiles();

        var visible = service.GetVisible(profiles);

        Assert.Equal(["local:0", "vault:one", "local:1", "vault:two"], visible.Select(profile => profile.Key));
        Assert.DoesNotContain(visible, profile => profile.Source == ProfileLibrarySource.Cloud);
    }

    [Fact]
    public void GetVisible_StoredSelection_KeepsOrderAndNeverExceedsFour()
    {
        var (service, _) = CreateService("[\"cloud:only\",\"local:1\",\"vault:one\",\"local:0\",\"vault:two\"]", "");

        var visible = service.GetVisible(Profiles());

        Assert.Equal(["cloud:only", "local:1", "vault:one", "local:0"], visible.Select(profile => profile.Key));
    }

    [Fact]
    public void SaveVisible_DeduplicatesAndCapsSelectionAtFour()
    {
        var (service, settings) = CreateService("[]", "");

        service.SaveVisible(["local:0", "local:0", "vault:one", "cloud:only", "local:1", "vault:two"]);

        settings
            .Received(1)
            .Set(
                settings.CLOUD_VISIBLE_PROFILE_IDS,
                Arg.Is<string>(value =>
                    JsonSerializer
                        .Deserialize<List<string>>(value)!
                        .SequenceEqual(new[] { "local:0", "vault:one", "cloud:only", "local:1" })
                )
            );
    }

    [Fact]
    public void GetSyncSelected_ExcludesCloudOnlyProfiles()
    {
        var (service, _) = CreateService("[]", "[\"cloud:only\",\"local:1\",\"vault:one\"]");

        var selected = service.GetSyncSelected(Profiles());

        Assert.Equal(["local:1", "vault:one"], selected.Select(profile => profile.Key));
    }

    [Fact]
    public async Task GetAllAsync_EmptyGameSlotsAreNeverShownInPickers()
    {
        var runtimeSettings = SettingsTestUtils.InitializeSettingsRuntime(Path.GetTempPath());
        runtimeSettings.LOAD_PATH.Returns(new TestSetting("load"));
        var settings = Substitute.For<ISettingsManager>();
        var enabledSetting = new TestSetting("enabled", typeof(bool));
        settings.CLOUD_SYNC_ENABLED.Returns(enabledSetting);
        settings.Get<bool>(enabledSetting).Returns(false);
        var licenses = Substitute.For<IGameLicenseSingletonService>();
        licenses.LicenseCollection.Returns(new LicenseCollection { Users = Enumerable.Range(0, 4).Select(_ => EmptyLicense()).ToList() });
        var service = new CloudProfileLibraryService(
            licenses,
            Substitute.For<ICloudSyncService>(),
            settings,
            Substitute.For<IVirtualProfileVaultService>(),
            Substitute.For<IProfileCloudBindingService>()
        );

        var profiles = await service.GetAllAsync();

        Assert.Empty(profiles);
    }

    [Fact]
    public async Task GetAllAsync_MatchedCloudProfile_BindsThePhysicalSlotToItsRemoteId()
    {
        var runtimeSettings = SettingsTestUtils.InitializeSettingsRuntime(Path.GetTempPath());
        runtimeSettings.LOAD_PATH.Returns(new TestSetting("load"));
        var settings = Substitute.For<ISettingsManager>();
        var enabledSetting = new TestSetting("enabled", typeof(bool));
        settings.CLOUD_SYNC_ENABLED.Returns(enabledSetting);
        settings.Get<bool>(enabledSetting).Returns(true);
        var licenses = Substitute.For<IGameLicenseSingletonService>();
        licenses.LicenseCollection.Returns(new LicenseCollection { Users = [ActiveLicense()] });
        var cloudSync = Substitute.For<ICloudSyncService>();
        var remoteId = Guid.NewGuid();
        cloudSync
            .GetAvailableProfilesAsync()
            .Returns(
                [
                    new CloudProfileManifest
                    {
                        ProfileId = remoteId,
                        ProfileName = "Alex",
                        LicensePreviews = [new CloudLicensePreview(0, "Alex", "1234-5678-9012", 5000, 5000, true)],
                    },
                ]
            );
        var bindings = Substitute.For<IProfileCloudBindingService>();
        var service = new CloudProfileLibraryService(
            licenses,
            cloudSync,
            settings,
            Substitute.For<IVirtualProfileVaultService>(),
            bindings
        );

        var profiles = await service.GetAllAsync();

        Assert.Single(profiles);
        Assert.Equal(ProfileStorageState.CloudAndLocal, profiles[0].StorageState);
        await bindings.Received(1).BindAsync(0, remoteId);
    }

    public void Dispose() => SettingsTestUtils.ResetSettingsRuntime();

    private static (CloudProfileLibraryService Service, ISettingsManager Settings) CreateService(string visible, string sync)
    {
        var settings = Substitute.For<ISettingsManager>();
        var visibleSetting = new TestSetting("visible");
        var syncSetting = new TestSetting("sync");
        settings.CLOUD_VISIBLE_PROFILE_IDS.Returns(visibleSetting);
        settings.CLOUD_SYNC_PROFILE_IDS.Returns(syncSetting);
        settings.Get<string>(visibleSetting).Returns(visible);
        settings.Get<string>(syncSetting).Returns(sync);
        settings.Set(Arg.Any<Setting>(), Arg.Any<string>()).Returns(true);
        return (
            new CloudProfileLibraryService(
                Substitute.For<IGameLicenseSingletonService>(),
                Substitute.For<ICloudSyncService>(),
                settings,
                Substitute.For<IVirtualProfileVaultService>(),
                Substitute.For<IProfileCloudBindingService>()
            ),
            settings
        );
    }

    private static IReadOnlyList<ProfileLibraryEntry> Profiles() =>
        [
            Entry("local:0", ProfileLibrarySource.Local),
            Entry("vault:one", ProfileLibrarySource.Vault),
            Entry("cloud:only", ProfileLibrarySource.Cloud),
            Entry("local:1", ProfileLibrarySource.Local),
            Entry("vault:two", ProfileLibrarySource.Vault),
        ];

    private static ProfileLibraryEntry Entry(string key, ProfileLibrarySource source) =>
        new(
            key,
            source,
            source == ProfileLibrarySource.Cloud ? ProfileStorageState.CloudOnly : ProfileStorageState.LocalOnly,
            key,
            "",
            null,
            null
        );

    private static LicenseProfile EmptyLicense() =>
        new()
        {
            FriendCode = "0000-0000-0000",
            Mii = new Mii { Name = new MiiName(SettingValues.NoLicense) },
            Vr = 5000,
            Br = 5000,
            RegionId = 10,
            TotalRaceCount = 0,
            TotalWinCount = 0,
        };

    private static LicenseProfile ActiveLicense() =>
        new()
        {
            FriendCode = "1234-5678-9012",
            Mii = new Mii { Name = new MiiName("Alex") },
            Vr = 5000,
            Br = 5000,
            RegionId = 10,
            TotalRaceCount = 1,
            TotalWinCount = 1,
        };

    private sealed class TestSetting(string name, Type? type = null)
        : Setting(type ?? typeof(string), name, type == typeof(bool) ? false : string.Empty)
    {
        protected override bool SetInternal(object newValue, bool skipSave = false) => true;

        public override object Get() => string.Empty;

        public override bool IsValid() => true;
    }
}
