using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Testably.Abstractions.Testing;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Models.Enums;
using WheelWizard.RrRooms;
using WheelWizard.Settings;
using WheelWizard.Shared.Binary;
using WheelWizard.Shared.Polling;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;

namespace WheelWizard.Test.Features.WiiManagement;

public class RegionLicenseTests
{
    [Fact]
    public void RegionSummaries_KeepSparseSlotIndexes_AndDoNotChangeActiveData()
    {
        var (service, fs, settings) = Create();
        AddSave(fs, MarioKartWiiEnums.Regions.Europe, 0, 3);
        AddSave(fs, MarioKartWiiEnums.Regions.Japan, 2);
        var users = service.LicenseCollection.Users;
        var profiles = service.GetRegionLicenses();
        Assert.Equal(3, profiles.Count);
        Assert.Equal(new[] { 0, 3 }, profiles.Where(p => p.Region == MarioKartWiiEnums.Regions.Europe).Select(p => p.Slot));
        Assert.Equal("Slot 3", profiles.Single(p => p.Region == MarioKartWiiEnums.Regions.Japan).Name);
        Assert.Same(users, service.LicenseCollection.Users);
        Assert.Empty(users);
        Assert.DoesNotContain(settings.ReceivedCalls(), call => call.GetMethodInfo().Name == "Set");
    }

    [Fact]
    public void RegionSummaries_IncludeAtMostFourProfilesPerRegion()
    {
        var (service, fs, _) = Create();
        foreach (var region in Enum.GetValues<MarioKartWiiEnums.Regions>().Where(r => r != MarioKartWiiEnums.Regions.None))
            AddSave(fs, region, 0, 1, 2, 3);
        Assert.Equal(16, service.GetRegionLicenses().Count);
    }

    [Fact]
    public void RegionSummaries_IgnoreTruncatedAndEmptySaves()
    {
        var (service, fs, _) = Create();
        AddSave(fs, MarioKartWiiEnums.Regions.Europe);
        fs.Directory.CreateDirectory("/saves/RMCJ");
        fs.File.WriteAllText("/saves/RMCJ/rksys.dat", "RKSD0006");
        Assert.Empty(service.GetRegionLicenses());
    }

    private static void AddSave(MockFileSystem fs, MarioKartWiiEnums.Regions region, params int[] slots)
    {
        var data = new byte[0x2BC000];
        Encoding.ASCII.GetBytes("RKSD0006").CopyTo(data, 0);
        foreach (var slot in slots)
        {
            var offset = 8 + slot * 0x8CC0;
            Encoding.ASCII.GetBytes("RKPD").CopyTo(data, offset);
            Encoding.BigEndianUnicode.GetBytes($"Slot {slot + 1}").CopyTo(data, offset + 0x14);
            BigEndianBinary.WriteUInt32BigEndian(data, offset + 0x5C, (uint)(slot + 1));
        }
        var path = $"/saves/{GameRegion.GetGameId(region)}/rksys.dat";
        fs.Directory.CreateDirectory(fs.Path.GetDirectoryName(path)!);
        fs.File.WriteAllBytes(path, data);
    }

    private static (GameLicenseSingletonService, MockFileSystem, ISettingsManager) Create()
    {
        var fs = new MockFileSystem();
        var paths = Substitute.For<ICustomDistributionPaths>();
        paths.SaveFolderPath.Returns("/saves");
        var settings = Substitute.For<ISettingsManager>();
        var miis = Substitute.For<IMiiDbService>();
        miis.GetByAvatarId(Arg.Any<uint>()).Returns(Fail("Missing Mii"));
        var service = new GameLicenseSingletonService(miis, fs,
            Substitute.For<IWhWzDataSingletonService>(), Substitute.For<IRrRatingReader>(), settings,
            Substitute.For<ISaveRegionService>(), Substitute.For<IDolphinPaths>(), paths,
            Substitute.For<IRoomPresence>(), Substitute.For<IPollingScheduler>(), System.TimeProvider.System,
            NullLogger<GameLicenseSingletonService>.Instance);
        return (service, fs, settings);
    }
}
