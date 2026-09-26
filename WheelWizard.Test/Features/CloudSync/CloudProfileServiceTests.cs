using WheelWizard.CloudSync;
using WheelWizard.CloudSync.Backup;
using WheelWizard.CloudSync.Mii;
using WheelWizard.CloudSync.Profile;
using WheelWizard.CustomDistributions;
using WheelWizard.Settings;

namespace WheelWizard.Test.Features.CloudSync;

public sealed class CloudProfileServiceTests
{
    private readonly CloudProfileService _service = new(
        Substitute.For<IMiiProfileService>(),
        Substitute.For<IProfileBackupService>(),
        Substitute.For<ICustomDistributionSingletonService>(),
        Substitute.For<ISettingsManager>()
    );

    [Fact]
    public async Task WriteAndReadPackageAsync_RoundTripsOnlyPortableProfileFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"wheelwizard-cloud-test-{Guid.NewGuid():N}");
        var archivePath = Path.Combine(folder, "profile.zip");
        var package = CreatePackage();

        try
        {
            await _service.WritePackageAsync(package, archivePath);
            var restored = await _service.ReadPackageAsync(archivePath);

            await _service.ValidateProfileAsync(restored);
            Assert.Equal(package.ProfileId, restored.ProfileId);
            Assert.Equal(package.RksysData, restored.RksysData);
            Assert.Equal(package.MiiData, restored.MiiData);
            Assert.Equal(package.RrRatingData, restored.RrRatingData);
            Assert.Equal(package.RrSettingsData, restored.RrSettingsData);
            Assert.Equal(package.RrGameSettingsData, restored.RrGameSettingsData);
            Assert.Equal(package.GhostData["race.rkg"], restored.GhostData["race.rkg"]);
            Assert.Equal(package.GhostData["folder/ghost.rkg"], restored.GhostData["folder/ghost.rkg"]);
            Assert.DoesNotContain("NAND", restored.Manifest.Files.Select(file => file.LogicalName), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateProfileAsync_RejectsTamperedContentBeforeApply()
    {
        var package = CreatePackage();
        package.RksysData[0] ^= 0xff;

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ValidateProfileAsync(package));
    }

    [Fact]
    public async Task ValidateProfileAsync_RejectsAmbiguousMissingMii()
    {
        var package = CreatePackage(includeMii: false, miiMissingOnSource: false);

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ValidateProfileAsync(package));
    }

    [Fact]
    public async Task ValidateProfileAsync_RejectsUnsafeGhostPath()
    {
        var package = CreatePackage(ghosts: new Dictionary<string, byte[]> { ["../outside.rkg"] = [0x01] });

        await Assert.ThrowsAsync<InvalidDataException>(() => _service.ValidateProfileAsync(package));
    }

    [Fact]
    public void CreateManifest_UsesStableHashRegardlessOfGhostDictionaryOrder()
    {
        var first = CreatePackage(ghosts: new Dictionary<string, byte[]> { ["b.rkg"] = [0x02], ["a.rkg"] = [0x01] });
        var second = CreatePackage(
            ghosts: new Dictionary<string, byte[]> { ["a.rkg"] = [0x01], ["b.rkg"] = [0x02] },
            profileId: first.ProfileId
        );

        var deviceId = Guid.NewGuid();
        var firstManifest = CloudProfileService.CreateManifest(first, first.ProfileId, deviceId, 1, []);
        var secondManifest = CloudProfileService.CreateManifest(second, second.ProfileId, deviceId, 1, []);

        Assert.Equal(firstManifest.ContentHash, secondManifest.ContentHash);
        Assert.Equal(firstManifest.Files.Select(file => file.LogicalName), secondManifest.Files.Select(file => file.LogicalName));
    }

    private static CloudProfilePackage CreatePackage(
        byte[]? mii = null,
        bool includeMii = true,
        bool miiMissingOnSource = false,
        IReadOnlyDictionary<string, byte[]>? ghosts = null,
        Guid? profileId = null
    )
    {
        var id = profileId ?? Guid.NewGuid();
        var package = new CloudProfilePackage
        {
            ProfileId = id,
            ProfileName = "Test license",
            RksysData = [0x52, 0x4b, 0x53, 0x59, 0x53],
            MiiData = includeMii ? mii ?? new byte[74] : null,
            MiiMissingOnSource = miiMissingOnSource,
            RrRatingData = [0x10],
            RrSettingsData = [0x11],
            RrGameSettingsData = [0x12],
            GhostData = ghosts ?? new Dictionary<string, byte[]> { ["race.rkg"] = [0x20], ["folder/ghost.rkg"] = [0x21] },
            Manifest = new CloudProfileManifest { ProfileId = id },
        };
        package.Manifest = CloudProfileService.CreateManifest(package, id, Guid.NewGuid(), 3, []);
        return package;
    }
}
