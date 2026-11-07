using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using WheelWizard.CustomDistributions;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Models.Enums;
using WheelWizard.RrRooms;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Binary;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Polling;
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement.FriendCodes;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.GameLicense;

// big big thanks to https://kazuki-4ys.github.io/web_apps/FaceThief/ for the JS implementation
// Also Refer to this documentation https://wiki.tockdom.com/wiki/Rksys.dat
public sealed record RegionLicense(MarioKartWiiEnums.Regions Region, int Slot, string Name, string FriendCode);

public interface IGameLicenseSingletonService
{
    /// <summary>
    /// Gets the currently loaded <see cref="Domain.LicenseCollection"/>.
    /// </summary>
    LicenseCollection LicenseCollection { get; }

    /// <summary>
    /// Loads the game data from the rksys.dat file.
    /// </summary>
    OperationResult LoadLicense();

    IReadOnlyList<RegionLicense> GetRegionLicenses();

    /// <summary>
    /// Retrieves the user data for a specific index.
    /// </summary>
    /// <param name="index">Index of user (1-3)</param>
    LicenseProfile GetUserData(int index);

    /// <summary>
    /// Gets the currently selected user.
    /// </summary>
    LicenseProfile ActiveUser { get; }

    /// <summary>
    /// Gets the list of friends for the currently selected user.
    /// </summary>
    List<FriendProfile> ActiveCurrentFriends { get; }

    /// <summary>
    /// Checks if any user is valid (i.e., has a non-empty friend code).
    /// </summary>
    bool HasAnyValidUsers { get; }

    /// <summary>
    /// Refreshes the online status of the users based on the current live rooms.
    /// </summary>
    void RefreshOnlineStatus();

    /// <summary>
    /// Changes the name of a Mii for a specific user index.
    /// </summary>
    OperationResult ChangeMiiName(int userIndex, string newName);

    /// <summary>
    /// Subscribes a listener to the repeated task manager.
    /// </summary>
    void Subscribe(IPollingListener subscriber);
    bool Unsubscribe(IPollingListener subscriber);

    /// <summary>
    /// Changes the Mii for a specific user index.
    /// </summary>
    OperationResult ChangeMii(int userIndex, Mii? newMii);

    /// <summary>
    /// Adds a friend to the specified license slot in rksys.dat.
    /// </summary>
    OperationResult AddFriend(int userIndex, string friendCode, Mii friendMii, uint vr = 5000, uint br = 5000);

    /// <summary>
    /// Removes a friend from the specified license slot in rksys.dat.
    /// </summary>
    OperationResult RemoveFriend(int userIndex, string friendCode);
}

public class GameLicenseSingletonService : ObservablePollingService, IGameLicenseSingletonService
{
    private readonly IMiiDbService _miiService;
    private readonly IFileSystem _fileSystem;
    private readonly IRrBadgeSingletonService _badgeService;
    private readonly IRrRatingReader _rrratingReader;
    private readonly ISettingsManager _settingsManager;
    private readonly ISaveRegionService _saveRegions;
    private readonly IDolphinPaths _dolphinPaths;
    private readonly ICustomDistributionPaths _distributionPaths;
    private readonly IRoomPresence _presence;
    private readonly IProcessLauncher _processes;
    private readonly IRuntimeEnvironment _environment;
    private readonly IUnixProcessService _unixProcesses;
    private readonly ILogger<GameLicenseSingletonService> _logger;
    private LicenseCollection Licenses { get; }
    private byte[]? _rksysData;

    public GameLicenseSingletonService(
        IMiiDbService miiService,
        IFileSystem fileSystem,
        IRrBadgeSingletonService badgeService,
        IRrRatingReader rrratingReader,
        ISettingsManager settingsManager,
        ISaveRegionService saveRegions,
        IDolphinPaths dolphinPaths,
        ICustomDistributionPaths distributionPaths,
        IRoomPresence presence,
        IProcessLauncher processes,
        IRuntimeEnvironment environment,
        IUnixProcessService unixProcesses,
        IPollingScheduler scheduler,
        TimeProvider timeProvider,
        ILogger<GameLicenseSingletonService> logger
    )
        : base(40, scheduler, timeProvider, logger)
    {
        _miiService = miiService;
        _fileSystem = fileSystem;
        _badgeService = badgeService;
        _rrratingReader = rrratingReader;
        _settingsManager = settingsManager;
        _saveRegions = saveRegions;
        _dolphinPaths = dolphinPaths;
        _distributionPaths = distributionPaths;
        _presence = presence;
        _processes = processes;
        _environment = environment;
        _unixProcesses = unixProcesses;
        _logger = logger;
        Licenses = new();
    }

    private const int RksysSize = 0x2BC000;
    private const string RksysMagic = "RKSD0006";
    private const int MaxPlayerNum = 4;
    private const int RkpdSize = 0x8CC0;
    private const string RkpdMagic = "RKPD";
    private const int MaxFriendNum = 30;
    private const int FriendDataOffset = 0x56D0;
    private const int FriendDataSize = 0x1C0;
    private const int FriendSecondaryDataOffset = 0x8B50;
    private const int FriendSecondaryDataSize = 0x0C;
    private const int MiiSize = 0x4A;

    // Friend flags: bit0 the entry is in use, bit1 the game still has to download the friend's info.
    private const ushort FriendFlagAdded = 0x0001;
    private const ushort FriendFlagInfoPending = 0x0002;
    private const int FriendDwcIndexOffset = 0x66;

    // First word of the DWC friend data: bits 11-12 hold the type, bit 13 is set once the friend added you back.
    private const uint DwcFriendTypeMask = 0x1800;
    private const uint DwcFriendTypeFriendKey = 0x1000;
    private const uint DwcFriendTypeProfileId = 0x1800;
    private const uint DwcFriendBuddyFlag = 0x2000;

    // Retro Rewind stores a friend's VR/BR in rksys.dat as whole hundreds (29,367 VR is stored as 293).
    private const uint FriendRatingScale = 100;
    private const uint DefaultRating = 5000;
    private const uint MaxRating = 1_000_000;

    /// <summary>
    /// Turns a friend's VR/BR from rksys.dat into rating points. Older WheelWizard versions stored full points instead,
    /// so the default (5000) and anything above the highest stored rating are already points.
    /// </summary>
    public static uint FriendRatingFromRksys(uint stored) =>
        stored == DefaultRating || stored > MaxRating / FriendRatingScale ? stored : stored * FriendRatingScale;

    /// <summary>Turns rating points into the friend VR/BR stored in rksys.dat.</summary>
    public static ushort FriendRatingToRksys(uint points) => (ushort)(Math.Min(points, MaxRating) / FriendRatingScale);

    /// <summary>
    /// Returns the "focused" or currently active license/user as determined by the Settings.
    /// </summary>
    public LicenseProfile ActiveUser => Licenses.Users[_settingsManager.Get<int>(_settingsManager.FOCUSED_USER)];

    public List<FriendProfile> ActiveCurrentFriends => Licenses.Users[_settingsManager.Get<int>(_settingsManager.FOCUSED_USER)].Friends;

    public LicenseCollection LicenseCollection => Licenses;

    public LicenseProfile GetUserData(int index) => Licenses.Users[index];

    public bool HasAnyValidUsers => Licenses.Users.Any(user => user.FriendCode != "0000-0000-0000");

    public void RefreshOnlineStatus()
    {
        foreach (var user in Licenses.Users)
        {
            user.IsOnline = _presence.IsOnline(user.FriendCode);
            foreach (var friend in user.Friends)
                friend.IsOnline = _presence.IsOnline(friend.FriendCode);
        }
    }

    // Read menu summaries without changing the active region or loaded save data.
    public IReadOnlyList<RegionLicense> GetRegionLicenses()
    {
        var result = new List<RegionLicense>();
        foreach (var region in Enum.GetValues<MarioKartWiiEnums.Regions>().Where(r => r != MarioKartWiiEnums.Regions.None))
        {
            try
            {
                var path = _fileSystem.Path.Combine(_distributionPaths.SaveFolderPath, GameRegion.GetGameId(region), "rksys.dat");
                if (!_fileSystem.File.Exists(path))
                    continue;
                var data = _fileSystem.File.ReadAllBytes(path);
                if (data.Length != RksysSize || Encoding.ASCII.GetString(data, 0, RksysMagic.Length) != RksysMagic)
                    continue;
                for (var slot = 0; slot < MaxPlayerNum; slot++)
                {
                    var offset = RksysMagic.Length + slot * RkpdSize;
                    if (Encoding.ASCII.GetString(data, offset, RkpdMagic.Length) != RkpdMagic)
                        continue;
                    var friendCode = FriendCode.GetFriendCode(data, offset + 0x5C);
                    if (string.IsNullOrEmpty(friendCode) || friendCode == "0000-0000-0000")
                        continue;
                    var mii = _miiService.GetByAvatarId(BigEndianBinary.BufferToUint32(data, offset + 0x28));
                    var name = mii.IsSuccess ? mii.Value.Name.ToString() : BigEndianBinary.GetUtf16String(data, offset + 0x14, 10);
                    result.Add(new(region, slot, name, friendCode));
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }

    public OperationResult LoadLicense()
    {
        var loadSaveDataResult = ReadRksys();
        _rksysData = loadSaveDataResult.IsSuccess ? loadSaveDataResult.Value : null;
        if (_rksysData != null && ValidateMagicNumber())
        {
            return ParseUsers();
        }

        // If the file was invalid or not found, create 4 dummy licenses
        Licenses.Users.Clear();
        for (var i = 0; i < MaxPlayerNum; i++)
            Licenses.Users.Add(CreateDummyLicense());
        return Ok();
    }

    private static LicenseProfile CreateDummyLicense()
    {
        var dummyLicense = new LicenseProfile
        {
            FriendCode = "0000-0000-0000",
            Mii = new() { Name = new MiiName(SettingValues.NoLicense) },
            Vr = 5000,
            Br = 5000,
            TotalRaceCount = 0,
            TotalWinCount = 0,
            Friends = [],
            RegionId = 10, // 10 => “unknown”
            IsOnline = false,
            Statistics = new(),
        };
        return dummyLicense;
    }

    private OperationResult ParseUsers()
    {
        Licenses.Users.Clear();
        if (_rksysData == null)
            return new ArgumentNullException(nameof(_rksysData));

        for (var i = 0; i < MaxPlayerNum; i++)
        {
            var rkpdOffset = RksysMagic.Length + i * RkpdSize;
            var rkpdCheck = Encoding.ASCII.GetString(_rksysData, rkpdOffset, RkpdMagic.Length) == RkpdMagic;
            if (!rkpdCheck)
            {
                Licenses.Users.Add(CreateDummyLicense());
                continue;
            }

            var user = ParseLicenseUser(rkpdOffset);
            if (user.IsFailure)
            {
                Licenses.Users.Add(CreateDummyLicense());
                continue;
            }
            Licenses.Users.Add(user.Value);
        }

        // Keep this here so we always have 4 users if the code above were to be changed
        while (Licenses.Users.Count < 4)
        {
            Licenses.Users.Add(CreateDummyLicense());
        }
        RefreshOnlineStatus();
        return Ok();
    }

    private OperationResult<LicenseProfile> ParseLicenseUser(int rkpdOffset)
    {
        if (_rksysData == null)
            return new ArgumentNullException(nameof(_rksysData));

        var profileId = BigEndianBinary.BufferToUint32(_rksysData, rkpdOffset + 0x5C);
        var friendCode = FriendCode.GetFriendCode(_rksysData, rkpdOffset + 0x5C);
        var miiDataResult = ParseMiiData(rkpdOffset);
        var miiToUse = miiDataResult.IsFailure ? new() : miiDataResult.Value;

        var statistics = StatisticsSerializer.ParseStatistics(_rksysData, rkpdOffset);

        // Try to read VR/BR from RRRating.pul file
        var vrFromRksys = BigEndianBinary.BufferToUint16(_rksysData, rkpdOffset + 0xB0);
        var brFromRksys = BigEndianBinary.BufferToUint16(_rksysData, rkpdOffset + 0xB2);
        var vr = vrFromRksys;
        var br = brFromRksys;

        if (profileId > 0 && !string.IsNullOrEmpty(friendCode))
        {
            var rrRatingData = TryReadRRratingFile();
            if (rrRatingData != null)
            {
                // Try to find by profile_id first
                var rating = _rrratingReader.ReadRatingFromFile(rrRatingData, profileId);

                // Verify friend code matches if rating found
                if (rating.HasValue)
                {
                    // Calculate friend code from profile_id and compare with rksys friend code
                    var calculatedFriendCode = FriendCode.ProfileIdToFriendCode(profileId);
                    // Convert friend code string to ulong for comparison
                    var fcString = friendCode.Replace("-", "");
                    if (ulong.TryParse(fcString, out var fcDec))
                    {
                        // Compare friend codes - use rating if they match
                        if (fcDec == calculatedFriendCode)
                        {
                            // Convert float VR/BR to uint by multiplying by 100 (e.g., 258.62 -> 25862)
                            vr = (uint)Math.Round(rating.Value.vr * 100);
                            br = (uint)Math.Round(rating.Value.br * 100);
                        }
                    }
                }
            }
        }

        var user = new LicenseProfile
        {
            Mii = miiToUse,
            FriendCode = friendCode,
            Vr = vr,
            Br = br,
            TotalRaceCount = BigEndianBinary.BufferToUint32(_rksysData, rkpdOffset + 0xB4),
            TotalWinCount = BigEndianBinary.BufferToUint32(_rksysData, rkpdOffset + 0xDC),
            BadgeVariants = _badgeService.GetBadges(friendCode),
            // Region is often found near offset 0x23308 + 0x3802 in RKGD. This code is a partial guess.
            // In practice, region might be read differently depending on your rksys layout.
            RegionId = BigEndianBinary.BufferToUint16(_rksysData, 0x23308 + 0x3802) / 4096,
            Statistics = statistics,
        };

        ParseFriends(user, rkpdOffset);
        return user;
    }

    private byte[]? TryReadRRratingFile()
    {
        try
        {
            var rrRatingPath = _fileSystem.Path.GetRrRatingFilePath(_dolphinPaths.WiiFolderPath);
            if (_fileSystem.File.Exists(rrRatingPath))
            {
                return _fileSystem.File.ReadAllBytes(rrRatingPath);
            }
        }
        catch
        {
            // Silently fail if file doesn't exist or can't be read
        }
        return null;
    }

    private OperationResult<Mii> ParseMiiData(int rkpdOffset)
    {
        //https://wiki.tockdom.com/wiki/Rksys.dat#DWC_User_Data
        if (_rksysData == null)
            return new ArgumentNullException(nameof(_rksysData));

        // licenseName is NOT always the same as mii name, could be useful
        var licenseName = BigEndianBinary.GetUtf16String(_rksysData, rkpdOffset + 0x14, 10);
        // id of mii
        var avatarId = BigEndianBinary.BufferToUint32(_rksysData, rkpdOffset + 0x28);
        // id of the actual system
        var clientId = BigEndianBinary.BufferToUint32(_rksysData, rkpdOffset + 0x2C);

        var rawMiiResult = _miiService.GetByAvatarId(avatarId);
        if (rawMiiResult.IsFailure)
            return new FormatException("Failed to parse mii data: " + rawMiiResult.Error.Message);

        return rawMiiResult.Value;
    }

    private void ParseFriends(LicenseProfile licenseProfile, int userOffset)
    {
        if (_rksysData == null)
            return;

        for (var i = 0; i < MaxFriendNum; i++)
        {
            var currentOffset = FriendOffset(userOffset, i);
            if (!IsFriendAdded(_rksysData, currentOffset))
                continue;

            // Until the game has downloaded a friend's info the entry may not have a Mii yet.
            Mii? mii = null;
            if (HasMiiData(_rksysData, currentOffset))
            {
                var miiResult = MiiSerializer.Deserialize(_rksysData.AsSpan(currentOffset + 0x1A, MiiSize).ToArray());
                if (miiResult.IsSuccess)
                    mii = miiResult.Value;
            }

            var dwcOffset = DwcFriendOffset(userOffset, i);
            var isRegistered =
                IsRegisteredFriend(BigEndianBinary.BufferToUint32(_rksysData, dwcOffset))
                && DwcFriendProfileId(_rksysData, dwcOffset) == BigEndianBinary.BufferToUint32(_rksysData, currentOffset + 0x04);
            var friendCode = FriendCode.GetFriendCode(_rksysData, currentOffset + 4);
            var friend = new FriendProfile
            {
                Vr = FriendRatingFromRksys(BigEndianBinary.BufferToUint16(_rksysData, currentOffset + 0x16)),
                Br = FriendRatingFromRksys(BigEndianBinary.BufferToUint16(_rksysData, currentOffset + 0x18)),
                FriendCode = friendCode,
                Wins = BigEndianBinary.BufferToUint16(_rksysData, currentOffset + 0x14),
                Losses = BigEndianBinary.BufferToUint16(_rksysData, currentOffset + 0x12),
                CountryCode = _rksysData[currentOffset + 0x68],
                RegionId = _rksysData[currentOffset + 0x69],
                BadgeVariants = _badgeService.GetBadges(friendCode),
                IsPending = !isRegistered,
                Mii = mii,
            };
            licenseProfile.Friends.Add(friend);
        }
    }

    public OperationResult ChangeMii(int userIndex, Mii? newMii)
    {
        if (newMii is null)
            return Fail("Mii cannot be null.");
        if (userIndex is < 0 or >= MaxPlayerNum)
            return Fail("Invalid license index. Please select a valid license.");

        var serialised = MiiSerializer.Serialize(newMii);
        if (serialised.IsFailure)
            return serialised.Error;

        var existing = _miiService.GetByAvatarId(newMii.MiiId);
        if (existing.IsFailure)
            return existing.Error;

        var licence = Licenses.Users[userIndex];
        licence.Mii = newMii;

        if (_rksysData is null || _rksysData.Length < RksysSize)
            return Fail("Invalid or unloaded rksys.dat data.");

        var rkpdOffset = 0x08 + userIndex * RkpdSize;
        BigEndianBinary.WriteUInt32BigEndian(_rksysData, rkpdOffset + 0x28, newMii.MiiId); // Avatar ID

        var systemid = newMii.SystemId0 << 24 | newMii.SystemId1 << 16 | newMii.SystemId2 << 8 | newMii.SystemId3;

        BigEndianBinary.WriteUInt32BigEndian(_rksysData, rkpdOffset + 0x2C, (uint)systemid);

        var nameWrite = WriteLicenseNameToSaveData(userIndex, newMii.Name.ToString());
        if (nameWrite.IsFailure)
            return nameWrite.Error;

        var saveResult = SaveRksysToFile();
        if (saveResult.IsFailure)
            return saveResult.Error;

        return Ok();
    }

    public OperationResult AddFriend(int userIndex, string friendCode, Mii friendMii, uint vr = 5000, uint br = 5000)
    {
        if (friendMii is null)
            return Fail("Friend Mii cannot be null.");

        var normalizedFriendCode = NormalizeFriendCode(friendCode);
        if (normalizedFriendCode.IsFailure)
            return normalizedFriendCode.Error;

        var friendProfileId = FriendCode.FriendCodeToProfileId(normalizedFriendCode.Value);
        if (friendProfileId is 0 or > int.MaxValue)
            return Fail("Invalid friend code.");
        if (!FriendCode.TryParseFriendCode(normalizedFriendCode.Value, out var friendCodeValue))
            return Fail("Invalid friend code.");

        var friendMiiDataResult = MiiSerializer.Serialize(friendMii);
        if (friendMiiDataResult.IsFailure)
            return friendMiiDataResult.Error;

        return EditFriends(
            userIndex,
            (data, rkpdOffset, repairedProfileIds) =>
            {
                if (BigEndianBinary.BufferToUint32(data, rkpdOffset + 0x5C) == friendProfileId)
                    return Fail("You cannot add your own friend code.");

                if (HasFriend(data, rkpdOffset, friendProfileId))
                    return repairedProfileIds.Contains(friendProfileId) ? Ok() : Fail("This friend is already in your list.");

                ClearDwcFriendData(data, rkpdOffset, friendProfileId);
                var slotIndex = FindFirstEmptyFriendSlot(data, rkpdOffset);
                if (slotIndex < 0)
                    return Fail("Your friend list is full. Remove a friend and try again.");

                WriteFriendSlot(
                    data,
                    rkpdOffset,
                    slotIndex,
                    friendCodeValue,
                    friendProfileId,
                    friendMiiDataResult.Value,
                    FriendRatingToRksys(vr),
                    FriendRatingToRksys(br)
                );
                return Ok();
            }
        );
    }

    public OperationResult RemoveFriend(int userIndex, string friendCode)
    {
        var normalizedFriendCode = NormalizeFriendCode(friendCode);
        if (normalizedFriendCode.IsFailure)
            return normalizedFriendCode.Error;

        var friendProfileId = FriendCode.FriendCodeToProfileId(normalizedFriendCode.Value);
        if (friendProfileId == 0)
            return Fail("Invalid friend code.");

        return EditFriends(
            userIndex,
            (data, rkpdOffset, _) =>
            {
                var removed = false;
                for (var i = 0; i < MaxFriendNum; i++)
                {
                    var mainOffset = FriendOffset(rkpdOffset, i);
                    if (IsFriendAdded(data, mainOffset) && BigEndianBinary.BufferToUint32(data, mainOffset + 0x04) == friendProfileId)
                    {
                        Array.Clear(data, mainOffset, FriendDataSize);
                        removed = true;
                    }
                }

                ClearDwcFriendData(data, rkpdOffset, friendProfileId);
                return removed ? Ok() : Fail("Friend was not found in this license.");
            }
        );
    }

    // Publish the fresh save only after the atomic write succeeds.
    private OperationResult EditFriends(int userIndex, Func<byte[], int, IReadOnlySet<uint>, OperationResult> edit)
    {
        if (userIndex is < 0 or >= MaxPlayerNum || userIndex >= Licenses.Users.Count)
            return Fail("Invalid license index. Please select a valid license.");
        if (_rksysData is null || !_settingsManager.PathsSetupCorrectly())
            return Fail("Invalid or unloaded rksys.dat data.");
        if (IsDolphinRunning())
            return Fail("Close Dolphin before changing your friend list, the game would undo the change.");

        var path = RksysFilePath();
        var readResult = TryCatch(() => _fileSystem.File.ReadAllBytes(path), "Failed to load rksys.dat.");
        if (readResult.IsFailure)
            return readResult.Error;

        var data = readResult.Value;
        if (data.Length != RksysSize || Encoding.ASCII.GetString(data, 0, RksysMagic.Length) != RksysMagic)
            return Fail("rksys.dat is invalid, your friend list was not changed.");

        var rkpdOffset = RksysMagic.Length + userIndex * RkpdSize;
        var licenseProfileId = BigEndianBinary.BufferToUint32(data, rkpdOffset + 0x5C);
        if (
            Encoding.ASCII.GetString(data, rkpdOffset, RkpdMagic.Length) != RkpdMagic
            || licenseProfileId == 0
            || licenseProfileId != FriendCode.FriendCodeToProfileId(Licenses.Users[userIndex].FriendCode)
        )
            return Fail("This license changed since it was loaded. Please try again.");

        var repairedProfileIds = RepairFriendKeys(data, rkpdOffset);
        var editResult = edit(data, rkpdOffset, repairedProfileIds);
        if (editResult.IsFailure)
            return editResult.Error;

        FixRksysCrc(data);
        var saveResult = _fileSystem.WriteAllBytesAtomic(path, data, "Failed to save rksys.dat.");
        if (saveResult.IsFailure)
            return saveResult.Error;

        _rksysData = data;
        return ParseUsers();
    }

    // Repair only incomplete friend-key tokens with a matching main entry.
    private static HashSet<uint> RepairFriendKeys(byte[] data, int rkpdOffset)
    {
        var repaired = new HashSet<uint>();
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var mainOffset = FriendOffset(rkpdOffset, i);
            var flags = FriendFlags(data, mainOffset);
            if ((flags & (FriendFlagAdded | FriendFlagInfoPending)) != FriendFlagAdded || data[mainOffset + FriendDwcIndexOffset] != i)
                continue;

            var profileId = BigEndianBinary.BufferToUint32(data, mainOffset + 0x04);
            var friendKey = FriendKey(data, mainOffset);
            if (profileId == 0 || friendKey != FriendCode.ProfileIdToFriendCode(profileId))
                continue;

            var dwcOffset = DwcFriendOffset(rkpdOffset, i);
            if (
                BigEndianBinary.BufferToUint32(data, dwcOffset) != DwcFriendTypeFriendKey
                || BigEndianBinary.BufferToUint32(data, dwcOffset + 0x04) != profileId
                || BigEndianBinary.BufferToUint32(data, dwcOffset + 0x08) != 0
            )
                continue;

            // A key without checksum still works when either checksum DWC accepts is zero.
            var checksum = (uint)(friendKey >> 32);
            if (checksum == 0 || FriendCode.ProfileIdToCrc8Checksum(profileId) == 0)
                continue;

            BigEndianBinary.WriteUInt32BigEndian(data, dwcOffset + 0x08, checksum);
            // Schedule the game's friend-info download.
            BigEndianBinary.WriteUInt16BigEndian(data, mainOffset + 0x10, (ushort)(flags | FriendFlagInfoPending));
            repaired.Add(profileId);
        }

        return repaired;
    }

    private static int FriendOffset(int rkpdOffset, int index) => rkpdOffset + FriendDataOffset + index * FriendDataSize;

    private static int DwcFriendOffset(int rkpdOffset, int index) =>
        rkpdOffset + FriendSecondaryDataOffset + index * FriendSecondaryDataSize;

    private static ushort FriendFlags(byte[] data, int friendOffset) => (ushort)BigEndianBinary.BufferToUint16(data, friendOffset + 0x10);

    private static ulong FriendKey(byte[] data, int friendOffset) =>
        ((ulong)BigEndianBinary.BufferToUint32(data, friendOffset) << 32) | BigEndianBinary.BufferToUint32(data, friendOffset + 0x04);

    private static bool HasMiiData(byte[] data, int friendOffset) =>
        data.AsSpan(friendOffset + 0x1A, MiiSize).IndexOfAnyExcept((byte)0) >= 0;

    private static bool IsFriendAdded(byte[] data, int friendOffset) =>
        (FriendFlags(data, friendOffset) & FriendFlagAdded) != 0 && FriendKey(data, friendOffset) != 0;

    private static bool IsFriendSlotEmpty(byte[] data, int friendOffset) =>
        FriendKey(data, friendOffset) == 0 && (FriendFlags(data, friendOffset) & FriendFlagAdded) == 0 && !HasMiiData(data, friendOffset);

    /// <summary>A friend is only registered once both players added each other.</summary>
    private static bool IsRegisteredFriend(uint dwcFriendData) =>
        (dwcFriendData & DwcFriendTypeMask) == DwcFriendTypeProfileId && (dwcFriendData & DwcFriendBuddyFlag) != 0;

    /// <summary>The profile ID in DWC friend data, or 0 when it holds none.</summary>
    private static uint DwcFriendProfileId(byte[] data, int dwcOffset)
    {
        var type = BigEndianBinary.BufferToUint32(data, dwcOffset) & DwcFriendTypeMask;
        return type is DwcFriendTypeFriendKey or DwcFriendTypeProfileId ? BigEndianBinary.BufferToUint32(data, dwcOffset + 0x04) : 0;
    }

    private static bool HasFriend(byte[] data, int rkpdOffset, uint friendProfileId)
    {
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var mainOffset = FriendOffset(rkpdOffset, i);
            if (IsFriendAdded(data, mainOffset) && BigEndianBinary.BufferToUint32(data, mainOffset + 0x04) == friendProfileId)
                return true;
        }

        return false;
    }

    private static void ClearDwcFriendData(byte[] data, int rkpdOffset, uint friendProfileId)
    {
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var dwcOffset = DwcFriendOffset(rkpdOffset, i);
            if (DwcFriendProfileId(data, dwcOffset) == friendProfileId)
                Array.Clear(data, dwcOffset, FriendSecondaryDataSize);
        }
    }

    private static int FindFirstEmptyFriendSlot(byte[] data, int rkpdOffset)
    {
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var dwcType = BigEndianBinary.BufferToUint32(data, DwcFriendOffset(rkpdOffset, i)) & DwcFriendTypeMask;
            if (IsFriendSlotEmpty(data, FriendOffset(rkpdOffset, i)) && dwcType == 0)
                return i;
        }

        return -1;
    }

    /// <summary>Writes a friend the way the game adds one by friend code.</summary>
    private static void WriteFriendSlot(
        byte[] data,
        int rkpdOffset,
        int slotIndex,
        ulong friendCodeValue,
        uint friendProfileId,
        byte[] serializedMii,
        ushort vr,
        ushort br
    )
    {
        var mainOffset = FriendOffset(rkpdOffset, slotIndex);
        var secondaryOffset = DwcFriendOffset(rkpdOffset, slotIndex);

        Array.Clear(data, mainOffset, FriendDataSize);
        Array.Clear(data, secondaryOffset, FriendSecondaryDataSize);

        BigEndianBinary.WriteUInt32BigEndian(data, mainOffset, (uint)(friendCodeValue >> 32));
        BigEndianBinary.WriteUInt32BigEndian(data, mainOffset + 0x04, friendProfileId);
        BigEndianBinary.WriteUInt16BigEndian(data, mainOffset + 0x10, FriendFlagAdded | FriendFlagInfoPending);
        BigEndianBinary.WriteUInt16BigEndian(data, mainOffset + 0x16, vr);
        BigEndianBinary.WriteUInt16BigEndian(data, mainOffset + 0x18, br);

        // Only a preview, the game replaces it with the friend's own info once they added you back.
        Array.Copy(serializedMii, 0, data, mainOffset + 0x1A, MiiSize);
        var miiCrc = Crc.ComputeCrc16Ccitt(serializedMii, 0, serializedMii.Length);
        BigEndianBinary.WriteUInt16BigEndian(data, mainOffset + 0x64, miiCrc);
        data[mainOffset + FriendDwcIndexOffset] = (byte)slotIndex;

        // Friend key token: type, profile ID and the friend code checksum.
        BigEndianBinary.WriteUInt32BigEndian(data, secondaryOffset, DwcFriendTypeFriendKey);
        BigEndianBinary.WriteUInt32BigEndian(data, secondaryOffset + 0x04, friendProfileId);
        BigEndianBinary.WriteUInt32BigEndian(data, secondaryOffset + 0x08, (uint)(friendCodeValue >> 32));
    }

    private bool IsDolphinRunning()
    {
        try
        {
            var executable = _dolphinPaths.ExecutablePath;
            if (!_environment.IsWindows && ShellQuoting.TryUnquoteUnixArgument(executable, out var unquotedExecutable))
                executable = unquotedExecutable;
            var configuredName = _fileSystem.Path.GetFileNameWithoutExtension(executable);
            string[] processNames = _environment.IsLinux ? ["dolphin-emu", configuredName] : ["Dolphin", "dolphin-emu", configuredName];
            return processNames
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Any(name =>
                    _processes
                        .GetProcessIds(name)
                        .Any(processId =>
                            !_environment.IsLinux
                            || name.Equals("dolphin-emu", StringComparison.OrdinalIgnoreCase)
                            || IsConfiguredExecutable(processId, executable)
                        )
                );
        }
        catch (Exception e)
        {
            // Rather refuse the change than risk the game overwriting it.
            _logger.LogDebug(e, "Could not check whether Dolphin is running");
            return true;
        }
    }

    private bool IsConfiguredExecutable(int processId, string executable)
    {
        var runningExecutable = _processes.GetExecutablePath(processId);
        if (string.IsNullOrWhiteSpace(runningExecutable))
            throw new InvalidOperationException("The process executable is unavailable.");

        var resolution = _unixProcesses.Run(
            "/usr/bin/env",
            new[] { "realpath", "-e", "-z", "--", executable },
            out var resolvedPath,
            out _
        );
        if (resolution.IsFailure || resolution.Value != 0 || string.IsNullOrEmpty(resolvedPath))
            throw new IOException("The configured executable could not be resolved.");

        var configuredExecutable = resolvedPath.TrimEnd('\0');
        return _fileSystem
            .Path.GetFullPath(runningExecutable)
            .Equals(_fileSystem.Path.GetFullPath(configuredExecutable), StringComparison.Ordinal);
    }

    private static OperationResult<string> NormalizeFriendCode(string friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return Fail("Friend code cannot be empty.");

        var digits = new string(friendCode.Where(char.IsDigit).ToArray());
        if (digits.Length != 12 || !ulong.TryParse(digits, out _))
            return Fail("Friend code must be exactly 12 digits.");

        var formatted = $"{digits[..4]}-{digits.Substring(4, 4)}-{digits.Substring(8, 4)}";
        var profileId = FriendCode.FriendCodeToProfileId(formatted);
        if (profileId == 0)
            return Fail("Invalid friend code.");

        return formatted;
    }

    private bool ValidateMagicNumber()
    {
        if (_rksysData == null)
            return false;
        return Encoding.ASCII.GetString(_rksysData, 0, RksysMagic.Length) == RksysMagic;
    }

    private OperationResult<byte[]> ReadRksys()
    {
        try
        {
            if (!_fileSystem.Directory.Exists(_distributionPaths.SaveFolderPath))
                return Fail("Save folder not found");

            // Dolphin uses the save of the game's region, so follow the game file instead of letting the user pick.
            var currentRegion = _saveRegions.ResolveActiveRegion(_distributionPaths.SaveFolderPath);
            if (currentRegion == MarioKartWiiEnums.Regions.None)
                return Fail("No valid regions found");

            var previousRegion = _settingsManager.Get<MarioKartWiiEnums.Regions>(_settingsManager.RR_REGION);
            if (currentRegion != previousRegion)
            {
                _settingsManager.Set(_settingsManager.RR_REGION, currentRegion);
                // The focused slot belonged to the other region's save.
                if (previousRegion != MarioKartWiiEnums.Regions.None)
                    _settingsManager.Set(_settingsManager.FOCUSED_USER, 0);
            }

            var saveFileFolder = _fileSystem.Path.Combine(_distributionPaths.SaveFolderPath, GameRegion.GetGameId(currentRegion));
            var saveFile = _fileSystem.Directory.GetFiles(saveFileFolder, "rksys.dat", SearchOption.TopDirectoryOnly);
            if (saveFile.Length == 0)
                return Fail("rksys.dat not found");
            return _fileSystem.File.ReadAllBytes(saveFile[0]);
        }
        catch
        {
            return Fail("Failed to load rksys.dat");
        }
    }

    /// <summary>
    /// Fixes the MKWii save file by recalculating and inserting the CRC32 at 0x27FFC.
    /// </summary>
    public static void FixRksysCrc(byte[] rksysData)
    {
        if (rksysData == null || rksysData.Length < RksysSize)
            throw new ArgumentException("Invalid rksys.dat data");

        var lengthToCrc = 0x27FFC;
        var newCrc = Crc.ComputeCrc32(rksysData, 0, lengthToCrc);

        // 2) Write CRC at offset 0x27FFC in big-endian.
        BigEndianBinary.WriteUInt32BigEndian(rksysData, 0x27FFC, newCrc);
    }

    public OperationResult ChangeMiiName(int userIndex, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return Fail("Cannot set name to an empty name.");
        if (userIndex is < 0 or >= MaxPlayerNum)
            return Fail("Invalid license index. Please select a valid license.");

        var user = Licenses.Users[userIndex];
        var miiIsEmptyOrNoName = IsNoNameOrEmptyMii(user);

        if (miiIsEmptyOrNoName)
            return Fail("This license has no Mii data or is incomplete.\n" + "Please use the Mii Channel to create a Mii first.");

        if (user.Mii == null)
            return Fail("This license has no Mii data or is incomplete.\n" + "Please use the Mii Channel to create a Mii first.");

        newName = Regex.Replace(newName, @"\s+", " ");

        // Basic checks
        if (newName.Length is > 10 or < 3)
            return Fail("Names must be between 3 and 10 characters long.");

        if (newName.Length > 10)
            newName = newName.Substring(0, 10);
        var nameResult = MiiName.Create(newName);
        if (nameResult.IsFailure)
            return nameResult.Error;

        user.Mii.Name = nameResult.Value;
        var nameWrite = WriteLicenseNameToSaveData(userIndex, newName);
        if (nameWrite.IsFailure)
            return nameWrite.Error;
        var updated = _miiService.UpdateName(user.Mii.MiiId, newName);
        if (updated.IsFailure)
            return updated.Error;
        var rksysSaveResult = SaveRksysToFile();
        if (rksysSaveResult.IsFailure)
            return rksysSaveResult.Error;

        return Ok();
    }

    private bool IsNoNameOrEmptyMii(LicenseProfile user)
    {
        if (user?.Mii == null)
            return true;

        var name = user.Mii.Name;
        if (name.ToString() == "no name")
            return true;
        var raw = MiiSerializer.Serialize(user.Mii).Value;
        if (raw.Length != 74)
            return true; // Not valid
        if (raw.All(b => b == 0))
            return true;

        // Otherwise, it’s presumably valid
        return false;
    }

    private OperationResult WriteLicenseNameToSaveData(int userIndex, string newName)
    {
        if (_rksysData == null || _rksysData.Length < RksysSize)
            return Fail("Invalid save data");
        var rkpdOffset = 0x8 + userIndex * RkpdSize;
        var nameOffset = rkpdOffset + 0x14;
        var nameBytes = Encoding.BigEndianUnicode.GetBytes(newName);
        for (var i = 0; i < 20; i++)
            _rksysData[nameOffset + i] = 0;
        Array.Copy(nameBytes, 0, _rksysData, nameOffset, Math.Min(nameBytes.Length, 20));
        return Ok();
    }

    private OperationResult SaveRksysToFile()
    {
        if (_rksysData == null || !_settingsManager.PathsSetupCorrectly())
            return Fail("Invalid save data or config is not setup properly.");

        // Never write a save file with a wrong size, that would corrupt every license on it.
        if (_rksysData.Length != RksysSize)
            return Fail($"Refusing to save rksys.dat: expected {RksysSize} bytes but got {_rksysData.Length}.");

        FixRksysCrc(_rksysData);
        return _fileSystem.WriteAllBytesAtomic(RksysFilePath(), _rksysData, "Failed to save rksys.dat.");
    }

    private string RksysFilePath()
    {
        var currentRegion = _settingsManager.Get<MarioKartWiiEnums.Regions>(_settingsManager.RR_REGION);
        var saveFolder = _fileSystem.Path.Combine(_distributionPaths.SaveFolderPath, GameRegion.GetGameId(currentRegion));
        return _fileSystem.Path.Combine(saveFolder, "rksys.dat");
    }

    protected override Task ExecuteTaskAsync(CancellationToken cancellationToken)
    {
        var result = LoadLicense();
        if (result.IsFailure)
        {
            throw new(result.Error.Message);
        }

        return Task.CompletedTask;
    }
}
