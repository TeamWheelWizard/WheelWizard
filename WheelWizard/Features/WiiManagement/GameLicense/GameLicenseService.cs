using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Services;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Other;
using WheelWizard.Settings;
using WheelWizard.Settings.Domain;
using WheelWizard.Utilities.Generators;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement.GameLicense.Domain;
using WheelWizard.WiiManagement.MiiManagement;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.GameLicense;

// big big thanks to https://kazuki-4ys.github.io/web_apps/FaceThief/ for the JS implementation
// Also Refer to this documentation https://wiki.tockdom.com/wiki/Rksys.dat
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
    void Subscribe(IRepeatedTaskListener subscriber);

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

public class GameLicenseSingletonService : RepeatedTaskManager, IGameLicenseSingletonService
{
    private readonly IMiiDbService _miiService;
    private readonly IFileSystem _fileSystem;
    private readonly IWhWzDataSingletonService _whWzDataSingletonService;
    private readonly IRRratingReader _rrratingReader;
    private readonly ISettingsManager _settingsManager;
    private LicenseCollection Licenses { get; }
    private byte[]? _rksysData;

    public GameLicenseSingletonService(
        IMiiDbService miiService,
        IFileSystem fileSystem,
        IWhWzDataSingletonService whWzDataSingletonService,
        IRRratingReader rrratingReader,
        ISettingsManager settingsManager
    )
        : base(40)
    {
        _miiService = miiService;
        _fileSystem = fileSystem;
        _whWzDataSingletonService = whWzDataSingletonService;
        _rrratingReader = rrratingReader;
        _settingsManager = settingsManager;
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

    // Pending one-sided friend entry (bit0 set, bit1 clear).
    private const ushort FriendSlotStateAdded = 0x0001;
    private const byte DwcFriendControlTypeInvalid = 0x00;
    private const byte DwcFriendControlTypeFriendKey = 0x10;
    private const ushort DefaultFriendCityId = 0;
    private const byte DefaultFriendGameRegion = 0;
    private const byte DefaultFriendCountryCode = 0xFF;
    private const byte DefaultFriendRegionId = 0xFF;

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
        var currentRooms = RRLiveRooms.Instance.CurrentRooms;
        var onlinePlayers = currentRooms.SelectMany(room => room.Players).ToList();
        foreach (var user in Licenses.Users)
        {
            user.IsOnline = onlinePlayers.Any(player => player.FriendCode == user.FriendCode);
        }
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
        return Ok();
    }

    private OperationResult<LicenseProfile> ParseLicenseUser(int rkpdOffset)
    {
        if (_rksysData == null)
            return new ArgumentNullException(nameof(_rksysData));

        var profileId = BigEndianBinaryHelper.BufferToUint32(_rksysData, rkpdOffset + 0x5C);
        var friendCode = FriendCodeGenerator.GetFriendCode(_rksysData, rkpdOffset + 0x5C);
        var miiDataResult = ParseMiiData(rkpdOffset);
        var miiToUse = miiDataResult.IsFailure ? new() : miiDataResult.Value;

        var statistics = StatisticsSerializer.ParseStatistics(_rksysData, rkpdOffset);

        // Try to read VR/BR from RRRating.pul file
        var vrFromRksys = BigEndianBinaryHelper.BufferToUint16(_rksysData, rkpdOffset + 0xB0);
        var brFromRksys = BigEndianBinaryHelper.BufferToUint16(_rksysData, rkpdOffset + 0xB2);
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
                    var calculatedFriendCode = FriendCodeGenerator.ProfileIdToFriendCode(profileId);
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
            TotalRaceCount = BigEndianBinaryHelper.BufferToUint32(_rksysData, rkpdOffset + 0xB4),
            TotalWinCount = BigEndianBinaryHelper.BufferToUint32(_rksysData, rkpdOffset + 0xDC),
            BadgeVariants = _whWzDataSingletonService.GetBadges(friendCode),
            // Region is often found near offset 0x23308 + 0x3802 in RKGD. This code is a partial guess.
            // In practice, region might be read differently depending on your rksys layout.
            RegionId = BigEndianBinaryHelper.BufferToUint16(_rksysData, 0x23308 + 0x3802) / 4096,
            Statistics = statistics,
        };

        ParseFriends(user, rkpdOffset);
        return user;
    }

    private byte[]? TryReadRRratingFile()
    {
        try
        {
            var rrRatingPath = PathManager.RRratingFilePath;
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
        var licenseName = BigEndianBinaryHelper.GetUtf16String(_rksysData, rkpdOffset + 0x14, 10);
        // id of mii
        var avatarId = BigEndianBinaryHelper.BufferToUint32(_rksysData, rkpdOffset + 0x28);
        // id of the actual system
        var clientId = BigEndianBinaryHelper.BufferToUint32(_rksysData, rkpdOffset + 0x2C);

        var rawMiiResult = _miiService.GetByAvatarId(avatarId);
        if (rawMiiResult.IsFailure)
            return new FormatException("Failed to parse mii data: " + rawMiiResult.Error.Message);

        return rawMiiResult.Value;
    }

    private void ParseFriends(LicenseProfile licenseProfile, int userOffset)
    {
        if (_rksysData == null)
            return;

        var friendOffset = userOffset + FriendDataOffset;
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var currentOffset = friendOffset + i * FriendDataSize;
            if (!CheckForMiiData(currentOffset + 0x1A))
                continue;

            var statusFlags = (ushort)BigEndianBinaryHelper.BufferToUint16(_rksysData, currentOffset + 0x10);
            var baseSlotState = (ushort)(statusFlags & 0x0003);
            var secondaryOffset = userOffset + FriendSecondaryDataOffset + i * FriendSecondaryDataSize;
            var secondaryControlByte = _rksysData[secondaryOffset + 0x2];
            // Treat legacy one-sided entries with control byte 0x00 as pending as well.
            var isPending =
                baseSlotState == FriendSlotStateAdded
                && (secondaryControlByte == DwcFriendControlTypeFriendKey || secondaryControlByte == DwcFriendControlTypeInvalid);

            var rawMiiBytes = _rksysData.AsSpan(currentOffset + 0x1A, MiiSize).ToArray();
            var friendCode = FriendCodeGenerator.GetFriendCode(_rksysData, currentOffset + 4);
            var miiResult = MiiSerializer.Deserialize(rawMiiBytes);
            if (miiResult.IsFailure)
                continue;

            var friend = new FriendProfile
            {
                Vr = BigEndianBinaryHelper.BufferToUint16(_rksysData, currentOffset + 0x16),
                Br = BigEndianBinaryHelper.BufferToUint16(_rksysData, currentOffset + 0x18),
                FriendCode = friendCode,
                Wins = BigEndianBinaryHelper.BufferToUint16(_rksysData, currentOffset + 0x14),
                Losses = BigEndianBinaryHelper.BufferToUint16(_rksysData, currentOffset + 0x12),
                CountryCode = _rksysData[currentOffset + 0x68],
                RegionId = _rksysData[currentOffset + 0x69],
                BadgeVariants = _whWzDataSingletonService.GetBadges(friendCode),
                IsPending = isPending,
                Mii = miiResult.Value,
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
        BigEndianBinaryHelper.WriteUInt32BigEndian(_rksysData, rkpdOffset + 0x28, newMii.MiiId); // Avatar ID

        var systemid = newMii.SystemId0 << 24 | newMii.SystemId1 << 16 | newMii.SystemId2 << 8 | newMii.SystemId3;

        BigEndianBinaryHelper.WriteUInt32BigEndian(_rksysData, rkpdOffset + 0x2C, (uint)systemid);

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
        if (userIndex is < 0 or >= MaxPlayerNum)
            return Fail("Invalid license index. Please select a valid license.");
        if (friendMii is null)
            return Fail("Friend Mii cannot be null.");
        if (_rksysData is null || _rksysData.Length < RksysSize)
            return Fail("Invalid or unloaded rksys.dat data.");

        var normalizedFriendCode = NormalizeFriendCode(friendCode);
        if (normalizedFriendCode.IsFailure)
            return normalizedFriendCode.Error;

        var friendProfileId = FriendCodeGenerator.FriendCodeToProfileId(normalizedFriendCode.Value);
        if (friendProfileId == 0)
            return Fail("Invalid friend code.");
        if (!FriendCodeGenerator.TryParseFriendCode(normalizedFriendCode.Value, out var friendCodeValue))
            return Fail("Invalid friend code.");

        var selectedLicense = Licenses.Users[userIndex];
        var currentUserPid = FriendCodeGenerator.FriendCodeToProfileId(selectedLicense.FriendCode);
        if (currentUserPid != 0 && currentUserPid == friendProfileId)
            return Fail("You cannot add your own friend code.");

        var duplicateFriend = selectedLicense.Friends.Any(friend =>
        {
            var pid = FriendCodeGenerator.FriendCodeToProfileId(friend.FriendCode);
            return pid != 0 && pid == friendProfileId;
        });
        if (duplicateFriend)
            return Fail("This friend is already in your list.");

        var friendMiiDataResult = MiiSerializer.Serialize(friendMii);
        if (friendMiiDataResult.IsFailure)
            return friendMiiDataResult.Error;

        var rkpdOffset = RksysMagic.Length + userIndex * RkpdSize;
        var slotIndex = FindFirstEmptyFriendSlot(rkpdOffset);
        if (slotIndex < 0)
            return Fail("Your friend list is full. Remove a friend and try again.");

        WriteFriendSlot(
            rkpdOffset,
            slotIndex,
            friendCodeValue,
            friendProfileId,
            friendMiiDataResult.Value,
            (ushort)Math.Min(vr, ushort.MaxValue),
            (ushort)Math.Min(br, ushort.MaxValue)
        );

        var saveResult = SaveRksysToFile();
        if (saveResult.IsFailure)
            return saveResult.Error;

        return ParseUsers();
    }

    private bool CheckForMiiData(int offset)
    {
        if (_rksysData == null || offset < 0 || offset + MiiSize > _rksysData.Length)
            return false;

        // If the entire 0x4A bytes are zero, we treat it as empty / no Mii data
        for (var i = 0; i < MiiSize; i++)
        {
            if (_rksysData[offset + i] != 0)
                return true;
        }

        return false;
    }

    private int FindFirstEmptyFriendSlot(int rkpdOffset)
    {
        if (_rksysData == null)
            return -1;

        var friendOffset = rkpdOffset + FriendDataOffset;
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var currentOffset = friendOffset + i * FriendDataSize;
            if (IsFriendSlotEmpty(currentOffset))
                return i;
        }

        return -1;
    }

    private bool IsFriendSlotEmpty(int friendSlotOffset)
    {
        if (_rksysData == null)
            return true;

        var statusFlags = (ushort)BigEndianBinaryHelper.BufferToUint16(_rksysData, friendSlotOffset + 0x10);
        var profileId = BigEndianBinaryHelper.BufferToUint32(_rksysData, friendSlotOffset + 0x04);
        var hasMii = CheckForMiiData(friendSlotOffset + 0x1A);

        return profileId == 0 && (statusFlags & 0x0003) == 0 && !hasMii;
    }

    private void WriteFriendSlot(
        int rkpdOffset,
        int slotIndex,
        ulong friendCodeValue,
        uint friendProfileId,
        byte[] serializedMii,
        ushort vr,
        ushort br
    )
    {
        if (_rksysData == null)
            return;

        var mainOffset = rkpdOffset + FriendDataOffset + slotIndex * FriendDataSize;
        var secondaryOffset = rkpdOffset + FriendSecondaryDataOffset + slotIndex * FriendSecondaryDataSize;

        Array.Clear(_rksysData, mainOffset, FriendDataSize);
        Array.Clear(_rksysData, secondaryOffset, FriendSecondaryDataSize);

        // Friend identity key (high 32 bits + profile ID low 32 bits).
        BigEndianBinaryHelper.WriteUInt32BigEndian(_rksysData, mainOffset, (uint)(friendCodeValue >> 32));
        BigEndianBinaryHelper.WriteUInt32BigEndian(_rksysData, mainOffset + 0x04, friendProfileId);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x10, FriendSlotStateAdded);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x12, 0);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x14, 0);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x16, vr);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x18, br);

        Array.Copy(serializedMii, 0, _rksysData, mainOffset + 0x1A, MiiSize);
        var miiCrc = CrcHelper.ComputeCrc16Ccitt(serializedMii, 0, serializedMii.Length);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x64, miiCrc);

        _rksysData[mainOffset + 0x66] = (byte)slotIndex;
        _rksysData[mainOffset + 0x67] = DefaultFriendGameRegion;
        _rksysData[mainOffset + 0x68] = DefaultFriendCountryCode;
        _rksysData[mainOffset + 0x69] = DefaultFriendRegionId;
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x6A, DefaultFriendCityId);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x6C, 0);
        BigEndianBinaryHelper.WriteUInt16BigEndian(_rksysData, mainOffset + 0x6E, 0);

        for (var i = 0; i < 10; i++)
        {
            BigEndianBinaryHelper.WriteUInt32BigEndian(_rksysData, mainOffset + 0x70 + i * 8, 0xFFFFFFFF);
        }

        // One-sided pending friend request token.
        _rksysData[secondaryOffset + 0x2] = DwcFriendControlTypeFriendKey;
        BigEndianBinaryHelper.WriteUInt32BigEndian(_rksysData, secondaryOffset + 0x4, friendProfileId);
    }

    public OperationResult RemoveFriend(int userIndex, string friendCode)
    {
        if (userIndex is < 0 or >= MaxPlayerNum)
            return Fail("Invalid license index. Please select a valid license.");
        if (_rksysData is null || _rksysData.Length < RksysSize)
            return Fail("Invalid or unloaded rksys.dat data.");

        var normalizedFriendCode = NormalizeFriendCode(friendCode);
        if (normalizedFriendCode.IsFailure)
            return normalizedFriendCode.Error;

        var friendProfileId = FriendCodeGenerator.FriendCodeToProfileId(normalizedFriendCode.Value);
        if (friendProfileId == 0)
            return Fail("Invalid friend code.");

        var rkpdOffset = RksysMagic.Length + userIndex * RkpdSize;
        var slotIndex = FindFriendSlotByProfileId(rkpdOffset, friendProfileId);
        if (slotIndex < 0)
            return Fail("Friend was not found in this license.");

        var mainOffset = rkpdOffset + FriendDataOffset + slotIndex * FriendDataSize;
        var secondaryOffset = rkpdOffset + FriendSecondaryDataOffset + slotIndex * FriendSecondaryDataSize;
        Array.Clear(_rksysData, mainOffset, FriendDataSize);
        Array.Clear(_rksysData, secondaryOffset, FriendSecondaryDataSize);

        var saveResult = SaveRksysToFile();
        if (saveResult.IsFailure)
            return saveResult.Error;

        return ParseUsers();
    }

    private int FindFriendSlotByProfileId(int rkpdOffset, uint friendProfileId)
    {
        if (_rksysData == null || friendProfileId == 0)
            return -1;

        var friendOffset = rkpdOffset + FriendDataOffset;
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var currentOffset = friendOffset + i * FriendDataSize;
            var currentPid = BigEndianBinaryHelper.BufferToUint32(_rksysData, currentOffset + 0x04);
            if (currentPid == friendProfileId)
                return i;
        }

        return -1;
    }

    private static OperationResult<string> NormalizeFriendCode(string friendCode)
    {
        if (string.IsNullOrWhiteSpace(friendCode))
            return Fail("Friend code cannot be empty.");

        var digits = new string(friendCode.Where(char.IsDigit).ToArray());
        if (digits.Length != 12 || !ulong.TryParse(digits, out _))
            return Fail("Friend code must be exactly 12 digits.");

        var formatted = $"{digits[..4]}-{digits.Substring(4, 4)}-{digits.Substring(8, 4)}";
        var profileId = FriendCodeGenerator.FriendCodeToProfileId(formatted);
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
            if (!_fileSystem.Directory.Exists(PathManager.SaveFolderPath))
                return Fail("Save folder not found");

            var currentRegion = _settingsManager.Get<MarioKartWiiEnums.Regions>(_settingsManager.RR_REGION);
            if (currentRegion == MarioKartWiiEnums.Regions.None)
            {
                // Double check if there's at least one valid region
                var validRegions = RRRegionManager.GetValidRegions();
                if (validRegions.First() != MarioKartWiiEnums.Regions.None)
                {
                    currentRegion = validRegions.First();
                    _settingsManager.Set(_settingsManager.RR_REGION, currentRegion);
                }
                else
                {
                    return Fail("No valid regions found");
                }
            }

            var saveFileFolder = _fileSystem.Path.Combine(PathManager.SaveFolderPath, RRRegionManager.ConvertRegionToGameId(currentRegion));
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
        var newCrc = CrcHelper.ComputeCrc32(rksysData, 0, lengthToCrc);

        // 2) Write CRC at offset 0x27FFC in big-endian.
        BigEndianBinaryHelper.WriteUInt32BigEndian(rksysData, 0x27FFC, newCrc);
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
        FixRksysCrc(_rksysData);
        var currentRegion = _settingsManager.Get<MarioKartWiiEnums.Regions>(_settingsManager.RR_REGION);
        var saveFolder = _fileSystem.Path.Combine(PathManager.SaveFolderPath, RRRegionManager.ConvertRegionToGameId(currentRegion));
        var trySaveRksys = TryCatch(() =>
        {
            _fileSystem.Directory.CreateDirectory(saveFolder);
            var path = _fileSystem.Path.Combine(saveFolder, "rksys.dat");
            _fileSystem.File.WriteAllBytes(path, _rksysData);
        });
        if (trySaveRksys.IsFailure)
            return trySaveRksys.Error;
        return Ok();
    }

    protected override Task ExecuteTaskAsync()
    {
        var result = LoadLicense();
        if (result.IsFailure)
        {
            throw new(result.Error.Message);
        }

        return Task.CompletedTask;
    }
}
