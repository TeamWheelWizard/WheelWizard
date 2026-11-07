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
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement.FriendCodes;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Test.Features.WiiManagement;

public class FriendListTests
{
    private const string SavePath = "/saves/RMCP/rksys.dat";
    private const int RkpdOffset = 0x08;
    private const uint LicenseProfileId = 12345;

    private const string FriendFc = "3484-8484-8484";
    private const uint FriendPid = 592497508;
    private const uint FriendChecksum = 81;

    // Friend code checksum (MD5) of 0
    private const string ZeroChecksumFc = "0009-9999-9999";
    private const uint ZeroChecksumPid = 999999999;

    private static readonly Mii FriendMii = MiiSerializer
        .Deserialize("AAAAQgBlAGUAAAAAAAAAAAAAAAAAAEBAgeGIAcKv7BAABEJBMb0oogiMCEgUTbiNAIoAiiUFAAAAAAAAAAAAAAAAAAAAAAAAAAA=")
        .Value;

    [Fact]
    public void AddFriend_WritesTheEntryTheGameWritesForAFriendCode()
    {
        var (service, fs, _) = Create(NewSave());

        Assert.True(service.AddFriend(0, FriendFc, FriendMii, 29_367).IsSuccess);

        var data = fs.File.ReadAllBytes(SavePath);
        var main = MainOffset(0);
        Assert.Equal(new byte[] { 0x00, 0x00, 0x00, 0x51, 0x23, 0x50, 0xCB, 0x64 }, data[main..(main + 8)]);
        Assert.Equal(0x0003u, BigEndianBinary.BufferToUint16(data, main + 0x10));
        Assert.Equal(293u, BigEndianBinary.BufferToUint16(data, main + 0x16));
        Assert.Equal(0, data[main + 0x66]);
        Assert.All(data[(main + 0x67)..(main + 0x1C0)], b => Assert.Equal(0, b));
        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x10, 0x00, 0x23, 0x50, 0xCB, 0x64, 0x00, 0x00, 0x00, 0x51 },
            data[DwcOffset(0)..(DwcOffset(0) + 12)]
        );

        var friend = Assert.Single(service.ActiveCurrentFriends);
        Assert.Equal(FriendFc, friend.FriendCode);
        Assert.True(friend.IsPending);
    }

    [Fact]
    public void AddFriend_RejectsProfileIdsTheGamesFriendKeyResolverCannotUse()
    {
        var save = NewSave();
        var (service, fs, _) = Create(save);

        Assert.True(service.AddFriend(0, FriendCodeOf((uint)int.MaxValue + 1), FriendMii).IsFailure);

        Assert.Equal(save, fs.File.ReadAllBytes(SavePath));
        Assert.Empty(service.ActiveCurrentFriends);
    }

    [Fact]
    public void AddFriend_KeepsAZeroChecksumInTheFriendKey()
    {
        var (service, fs, _) = Create(NewSave());

        Assert.True(service.AddFriend(0, ZeroChecksumFc, FriendMii).IsSuccess);

        var data = fs.File.ReadAllBytes(SavePath);
        Assert.Equal(0x1000u, BigEndianBinary.BufferToUint32(data, DwcOffset(0)));
        Assert.Equal(ZeroChecksumPid, BigEndianBinary.BufferToUint32(data, DwcOffset(0) + 4));
        Assert.Equal(0u, BigEndianBinary.BufferToUint32(data, DwcOffset(0) + 8));
        Assert.Equal(0x0003u, BigEndianBinary.BufferToUint16(data, MainOffset(0) + 0x10));
    }

    [Theory]
    [InlineData((ushort)0x0003, 0x1000u, false, true)] // Added by the game, waiting for its info
    [InlineData((ushort)0x0003, 0x3800u, false, false)] // Registered, info not downloaded yet
    [InlineData((ushort)0x0001, 0x3800u, true, false)] // Registered
    [InlineData((ushort)0x0001, 0x1000u, true, true)] // Waiting for the friend to add back
    [InlineData((ushort)0x0001, 0x1800u, true, true)] // Known profile ID, but not added back
    public void LoadLicense_ListsFriendsByFlagsAndDerivesPendingFromDwc(ushort flags, uint dwcType, bool withMii, bool pending)
    {
        var save = NewSave();
        PutFriend(save, 0, FriendPid, flags, dwcType, withMii);

        var (service, _, _) = Create(save);

        var friend = Assert.Single(service.ActiveCurrentFriends);
        Assert.Equal(FriendFc, friend.FriendCode);
        Assert.Equal(pending, friend.IsPending);
        Assert.Equal(withMii, friend.Mii != null);
    }

    [Fact]
    public void AddFriend_RepairsAnExistingFriendWhenTheListIsFull()
    {
        var save = NewSave();
        PutReleasedWheelWizardFriend(save, 0, FriendPid);
        for (var slot = 1; slot < 30; slot++)
            PutFriend(save, slot, 1000 + (uint)slot, 0x0003, 0x1000, withMii: false);
        var (service, fs, _) = Create(save);

        Assert.True(service.AddFriend(0, FriendFc, FriendMii).IsSuccess);

        Assert.Equal(30, service.ActiveCurrentFriends.Count);
        Assert.Equal(FriendChecksum, BigEndianBinary.BufferToUint32(fs.File.ReadAllBytes(SavePath), DwcOffset(0) + 8));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    public void LoadLicense_UsesTheDwcSlotEvenWhenTheStoredIndexDiffers(int dwcIndex)
    {
        var save = NewSave();
        PutFriend(save, 0, FriendPid, 0x0001, 0x3800, withMii: false);
        PutFriend(save, 1, 2001, 0x0001, 0x3800, withMii: false);
        save[MainOffset(0) + 0x66] = (byte)dwcIndex;
        var (service, _, _) = Create(save);

        Assert.False(service.ActiveCurrentFriends[0].IsPending);
        Assert.False(service.ActiveCurrentFriends[1].IsPending);
    }

    [Fact]
    public void LoadLicense_KeepsFriendsPendingWhenTheirDwcTokenBelongsToSomeoneElse()
    {
        var save = NewSave();
        PutFriend(save, 0, FriendPid, 0x0001, 0x3800, withMii: false);
        PutFriend(save, 1, 2001, 0x0001, 0x3800, withMii: false);
        BigEndianBinary.WriteUInt32BigEndian(save, DwcOffset(0) + 4, 2001);
        var (service, _, _) = Create(save);

        Assert.True(service.ActiveCurrentFriends[0].IsPending);
        Assert.False(service.ActiveCurrentFriends[1].IsPending);
    }

    [Fact]
    public void LoadLicense_SkipsEntriesThatAreNotInUse()
    {
        var save = NewSave();
        PutFriend(save, 0, FriendPid, 0x0000, 0x3800, withMii: true);

        var (service, _, _) = Create(save);

        Assert.Empty(service.ActiveCurrentFriends);
    }

    [Fact]
    public void AddFriend_RejectsAFriendThatIsStillWaitingForItsInfo()
    {
        var save = NewSave();
        PutFriend(save, 0, FriendPid, 0x0003, 0x1000, withMii: false);
        var (service, fs, _) = Create(save);

        var result = service.AddFriend(0, FriendFc, FriendMii);

        Assert.True(result.IsFailure);
        Assert.Equal(save, fs.File.ReadAllBytes(SavePath));
    }

    [Fact]
    public void AddFriend_CountsFriendsWithoutMiiForAFullList()
    {
        var save = NewSave();
        for (var slot = 0; slot < 30; slot++)
            PutFriend(save, slot, 1000 + (uint)slot, 0x0003, 0x1000, withMii: false);
        var (service, fs, _) = Create(save);

        Assert.Equal(30, service.ActiveCurrentFriends.Count);
        Assert.True(service.AddFriend(0, FriendFc, FriendMii).IsFailure);
        Assert.Equal(save, fs.File.ReadAllBytes(SavePath));
    }

    [Fact]
    public void AddFriend_SkipsSlotsWhoseDwcDataIsInUse()
    {
        var save = NewSave();
        PutFriend(save, 0, 1000, 0x0001, 0x3800, withMii: true);
        // DWC data left behind without a friend entry
        BigEndianBinary.WriteUInt32BigEndian(save, DwcOffset(1), 0x3800);
        BigEndianBinary.WriteUInt32BigEndian(save, DwcOffset(1) + 4, 1001);
        PutFriend(save, 3, 1002, 0x0001, 0x3800, withMii: true);
        var (service, fs, _) = Create(save);

        Assert.True(service.AddFriend(0, FriendFc, FriendMii).IsSuccess);

        var data = fs.File.ReadAllBytes(SavePath);
        Assert.Equal(FriendPid, BigEndianBinary.BufferToUint32(data, MainOffset(2) + 4));
        Assert.Equal(save[DwcOffset(1)..(DwcOffset(1) + 12)], data[DwcOffset(1)..(DwcOffset(1) + 12)]);
        Assert.Equal(save[DwcOffset(3)..(DwcOffset(3) + 12)], data[DwcOffset(3)..(DwcOffset(3) + 12)]);
    }

    [Fact]
    public void AddFriend_RepairsFriendKeysWrittenWithoutChecksum_AndLeavesOtherEntriesAlone()
    {
        var crc8ZeroPid = Enumerable
            .Range(1, 100_000)
            .Select(pid => (uint)pid)
            .First(pid => FriendCode.ProfileIdToCrc8Checksum(pid) == 0 && FriendCode.ProfileIdToFriendCode(pid) >> 32 != 0);

        var save = NewSave();
        PutReleasedWheelWizardFriend(save, 0, FriendPid);
        PutFriend(save, 1, 2001, 0x0001, 0x3800, withMii: true);
        PutReleasedWheelWizardFriend(save, 2, 2002);
        // Older friend entries without DWC data, the game makes those itself
        BigEndianBinary.WriteUInt32BigEndian(save, DwcOffset(2), 0);
        BigEndianBinary.WriteUInt32BigEndian(save, DwcOffset(2) + 4, 0);
        PutFriend(save, 3, 2003, 0x0001, 0x3800, withMii: true);
        // Development builds wrote the profile ID without the checksum in the friend entry
        BigEndianBinary.WriteUInt32BigEndian(save, MainOffset(3), 0);
        PutReleasedWheelWizardFriend(save, 4, ZeroChecksumPid);
        PutReleasedWheelWizardFriend(save, 5, crc8ZeroPid);
        var (service, fs, _) = Create(save);

        Assert.Equal(save, fs.File.ReadAllBytes(SavePath));
        Assert.True(service.AddFriend(0, FriendCodeOf(2001), FriendMii).IsFailure);
        Assert.Equal(save, fs.File.ReadAllBytes(SavePath));

        var newFriendFc = FriendCodeOf(3000);
        Assert.True(service.AddFriend(0, newFriendFc, FriendMii).IsSuccess);

        var data = fs.File.ReadAllBytes(SavePath);
        Assert.Equal(FriendChecksum, BigEndianBinary.BufferToUint32(data, DwcOffset(0) + 8));
        Assert.Equal(0x0003u, BigEndianBinary.BufferToUint16(data, MainOffset(0) + 0x10));
        for (var slot = 1; slot <= 5; slot++)
        {
            Assert.Equal(save[MainOffset(slot)..(MainOffset(slot) + 0x1C0)], data[MainOffset(slot)..(MainOffset(slot) + 0x1C0)]);
            Assert.Equal(save[DwcOffset(slot)..(DwcOffset(slot) + 12)], data[DwcOffset(slot)..(DwcOffset(slot) + 12)]);
        }
        Assert.Equal(3000u, BigEndianBinary.BufferToUint32(data, MainOffset(6) + 4));
    }

    [Fact]
    public void AddFriend_OfAFriendWrittenWithoutChecksum_RepairsIt()
    {
        var save = NewSave();
        PutReleasedWheelWizardFriend(save, 0, FriendPid);
        var (service, fs, _) = Create(save);

        Assert.True(service.AddFriend(0, FriendFc, FriendMii).IsSuccess);

        var data = fs.File.ReadAllBytes(SavePath);
        Assert.Equal(FriendChecksum, BigEndianBinary.BufferToUint32(data, DwcOffset(0) + 8));
        Assert.Equal(0u, BigEndianBinary.BufferToUint32(data, MainOffset(1) + 4));
        Assert.Single(service.ActiveCurrentFriends);
    }

    [Fact]
    public void AddFriend_KeepsChangesTheGameSavedAfterLoading()
    {
        var (service, fs, _) = Create(NewSave());
        var updated = fs.File.ReadAllBytes(SavePath);
        PutFriend(updated, 0, 2001, 0x0003, 0x1000, withMii: false);
        Encoding.BigEndianUnicode.GetBytes("Renamed").CopyTo(updated, RkpdOffset + 0x14);
        fs.File.WriteAllBytes(SavePath, updated);

        Assert.True(service.AddFriend(0, FriendFc, FriendMii).IsSuccess);

        var data = fs.File.ReadAllBytes(SavePath);
        Assert.Equal(updated[MainOffset(0)..(MainOffset(0) + 0x1C0)], data[MainOffset(0)..(MainOffset(0) + 0x1C0)]);
        Assert.Equal(updated[(RkpdOffset + 0x14)..(RkpdOffset + 0x28)], data[(RkpdOffset + 0x14)..(RkpdOffset + 0x28)]);
        Assert.Equal(FriendPid, BigEndianBinary.BufferToUint32(data, MainOffset(1) + 4));
        Assert.Equal(2, service.ActiveCurrentFriends.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddFriend_DoesNothingWhenTheSaveOnDiskIsInvalid(bool truncated)
    {
        var save = NewSave();
        PutFriend(save, 0, 2001, 0x0001, 0x3800, withMii: true);
        var (service, fs, _) = Create(save);
        var invalid = truncated ? save[..0x1000] : (byte[])save.Clone();
        if (!truncated)
            Encoding.ASCII.GetBytes("XXXX").CopyTo(invalid, 0);
        fs.File.WriteAllBytes(SavePath, invalid);

        Assert.True(service.AddFriend(0, FriendFc, FriendMii).IsFailure);
        Assert.True(service.RemoveFriend(0, FriendCodeOf(2001)).IsFailure);

        Assert.Equal(invalid, fs.File.ReadAllBytes(SavePath));
        Assert.Single(service.ActiveCurrentFriends);
    }

    [Fact]
    public void FriendChanges_AreRefusedWhileDolphinIsRunning()
    {
        var save = NewSave();
        PutFriend(save, 0, 2001, 0x0001, 0x3800, withMii: true);
        var (service, fs, processes) = Create(save);
        processes.GetProcessIds("Dolphin").Returns([1234]);

        Assert.True(service.AddFriend(0, FriendFc, FriendMii).IsFailure);
        Assert.True(service.RemoveFriend(0, FriendCodeOf(2001)).IsFailure);

        Assert.Equal(save, fs.File.ReadAllBytes(SavePath));
        Assert.Single(service.ActiveCurrentFriends);
    }

    [Fact]
    public void FriendChanges_KeepLoadedDataWhenTheAtomicWriteFails()
    {
        var save = NewSave();
        PutReleasedWheelWizardFriend(save, 0, FriendPid);
        var (service, fs, _) = Create(save);
        var loadedFriend = Assert.Single(service.ActiveCurrentFriends);
        fs.Directory.CreateDirectory(SavePath + ".tmp");

        Assert.True(service.AddFriend(0, FriendCodeOf(3000), FriendMii).IsFailure);
        Assert.True(service.RemoveFriend(0, FriendFc).IsFailure);

        Assert.Equal(save, fs.File.ReadAllBytes(SavePath));
        Assert.Same(loadedFriend, Assert.Single(service.ActiveCurrentFriends));
        Assert.True(loadedFriend.IsPending);
    }

    [Fact]
    public void RemoveFriend_ClearsTheEntryAndItsDwcData_AndKeepsTheOtherSlots()
    {
        var save = NewSave();
        PutFriend(save, 0, 2001, 0x0001, 0x3800, withMii: true);
        PutFriend(save, 2, 2002, 0x0001, 0x3800, withMii: true);
        // Friend without Mii whose DWC data is in slot 1
        PutFriend(save, 3, FriendPid, 0x0003, 0x1000, withMii: false, dwcIndex: 1);
        var (service, fs, _) = Create(save);
        Assert.Equal(3, service.ActiveCurrentFriends.Count);

        Assert.True(service.RemoveFriend(0, FriendFc).IsSuccess);

        var data = fs.File.ReadAllBytes(SavePath);
        Assert.All(data[MainOffset(3)..(MainOffset(3) + 0x1C0)], b => Assert.Equal(0, b));
        Assert.All(data[DwcOffset(1)..(DwcOffset(1) + 12)], b => Assert.Equal(0, b));
        foreach (var slot in new[] { 0, 2 })
        {
            Assert.Equal(save[MainOffset(slot)..(MainOffset(slot) + 0x1C0)], data[MainOffset(slot)..(MainOffset(slot) + 0x1C0)]);
            Assert.Equal(save[DwcOffset(slot)..(DwcOffset(slot) + 12)], data[DwcOffset(slot)..(DwcOffset(slot) + 12)]);
        }
        Assert.Equal(new[] { FriendCodeOf(2001), FriendCodeOf(2002) }, service.ActiveCurrentFriends.Select(friend => friend.FriendCode));
    }

    private static int MainOffset(int slot) => RkpdOffset + 0x56D0 + slot * 0x1C0;

    private static int DwcOffset(int slot) => RkpdOffset + 0x8B50 + slot * 0x0C;

    private static string FriendCodeOf(uint profileId)
    {
        var digits = FriendCode.ProfileIdToFriendCode(profileId).ToString("D12");
        return $"{digits[..4]}-{digits[4..8]}-{digits[8..]}";
    }

    private static void PutFriend(byte[] data, int slot, uint profileId, ushort flags, uint dwcType, bool withMii, int? dwcIndex = null)
    {
        var main = MainOffset(slot);
        var friendKey = FriendCode.ProfileIdToFriendCode(profileId);
        BigEndianBinary.WriteUInt32BigEndian(data, main, (uint)(friendKey >> 32));
        BigEndianBinary.WriteUInt32BigEndian(data, main + 4, profileId);
        BigEndianBinary.WriteUInt16BigEndian(data, main + 0x10, flags);
        BigEndianBinary.WriteUInt16BigEndian(data, main + 0x16, 5000);
        BigEndianBinary.WriteUInt16BigEndian(data, main + 0x18, 5000);
        if (withMii)
            MiiSerializer.Serialize(FriendMii).Value.CopyTo(data, main + 0x1A);
        data[main + 0x66] = (byte)(dwcIndex ?? slot);

        var dwc = DwcOffset(dwcIndex ?? slot);
        BigEndianBinary.WriteUInt32BigEndian(data, dwc, dwcType);
        BigEndianBinary.WriteUInt32BigEndian(data, dwc + 4, profileId);
        BigEndianBinary.WriteUInt32BigEndian(data, dwc + 8, dwcType == 0x1000 ? (uint)(friendKey >> 32) : 0);
    }

    // The entry WheelWizard 2.4.0 and later wrote: no checksum in the DWC friend key.
    private static void PutReleasedWheelWizardFriend(byte[] data, int slot, uint profileId)
    {
        PutFriend(data, slot, profileId, 0x0001, 0x1000, withMii: true);
        var main = MainOffset(slot);
        data[main + 0x68] = 0xFF;
        data[main + 0x69] = 0xFF;
        for (var i = 0; i < 10; i++)
            BigEndianBinary.WriteUInt32BigEndian(data, main + 0x70 + i * 8, 0xFFFFFFFF);
        BigEndianBinary.WriteUInt32BigEndian(data, DwcOffset(slot) + 8, 0);
    }

    private static byte[] NewSave()
    {
        var data = new byte[0x2BC000];
        Encoding.ASCII.GetBytes("RKSD0006").CopyTo(data, 0);
        Encoding.ASCII.GetBytes("RKPD").CopyTo(data, RkpdOffset);
        BigEndianBinary.WriteUInt32BigEndian(data, RkpdOffset + 0x5C, LicenseProfileId);
        return data;
    }

    private static (GameLicenseSingletonService, MockFileSystem, IProcessLauncher) Create(byte[] save)
    {
        var fs = new MockFileSystem();
        fs.Directory.CreateDirectory("/saves/RMCP");
        fs.File.WriteAllBytes(SavePath, save);
        var paths = Substitute.For<ICustomDistributionPaths>();
        paths.SaveFolderPath.Returns("/saves");
        var settings = Substitute.For<ISettingsManager>();
        settings.PathsSetupCorrectly().Returns(true);
        settings.Get(settings.RR_REGION).Returns(MarioKartWiiEnums.Regions.Europe);
        var saveRegions = Substitute.For<ISaveRegionService>();
        saveRegions.ResolveActiveRegion(Arg.Any<string>()).Returns(MarioKartWiiEnums.Regions.Europe);
        var miis = Substitute.For<IMiiDbService>();
        miis.GetByAvatarId(Arg.Any<uint>()).Returns(Fail("Missing Mii"));
        var processes = Substitute.For<IProcessLauncher>();
        processes.GetProcessIds(Arg.Any<string>()).Returns([]);
        var service = new GameLicenseSingletonService(
            miis,
            fs,
            Substitute.For<IRrBadgeSingletonService>(),
            Substitute.For<IRrRatingReader>(),
            settings,
            saveRegions,
            Substitute.For<IDolphinPaths>(),
            paths,
            Substitute.For<IRoomPresence>(),
            processes,
            Substitute.For<IPollingScheduler>(),
            System.TimeProvider.System,
            NullLogger<GameLicenseSingletonService>.Instance
        );
        Assert.True(service.LoadLicense().IsSuccess);
        return (service, fs, processes);
    }
}
