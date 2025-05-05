using System.IO.Abstractions;
using System.Text;
using System.Text.RegularExpressions;
using WheelWizard.Models.Enums;
using WheelWizard.Models.GameData;
using WheelWizard.Services;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Other;
using WheelWizard.Services.Settings;
using WheelWizard.Services.WiiManagement.SaveData;
using WheelWizard.Utilities.Generators;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement;

// big big thanks to https://kazuki-4ys.github.io/web_apps/FaceThief/ for the JS implementation
// Also Refer to this documentation https://wiki.tockdom.com/wiki/Rksys.dat
public interface IGameLicenseSingletonService
{
    /// <summary>
    /// Gets the currently loaded <see cref="Models.GameData.LicenseCollection"/>.
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
}

public class GameLicenseSingletonService : RepeatedTaskManager, IGameLicenseSingletonService
{
    private readonly IMiiDbService _miiService;
    private readonly IFileSystem _fileSystem;
    private readonly IWhWzDataSingletonService _whWzDataSingletonService;
    private LicenseCollection Licenses { get; }
    private byte[]? _rksysData;

    public GameLicenseSingletonService(IMiiDbService miiService, IFileSystem fileSystem, IWhWzDataSingletonService whWzDataSingletonService)
        : base(40)
    {
        _miiService = miiService;
        _fileSystem = fileSystem;
        _whWzDataSingletonService = whWzDataSingletonService;
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
    private const int MiiSize = 0x4A;

    /// <summary>
    /// Returns the "focused" or currently active license/user as determined by the Settings.
    /// </summary>
    public LicenseProfile ActiveUser => Licenses.Users[(int)SettingsManager.FOCUSSED_USER.Get()];

    public List<FriendProfile> ActiveCurrentFriends => Licenses.Users[(int)SettingsManager.FOCUSSED_USER.Get()].Friends;

    public LicenseCollection LicenseCollection => Licenses;

    public LicenseProfile GetUserData(int index) => Licenses.Users[index];

    public bool HasAnyValidUsers => Licenses.Users.Any(user => user.FriendCode != "0000-0000-0000");

    public void RefreshOnlineStatus()
    {
        var currentRooms = RRLiveRooms.Instance.CurrentRooms;
        var onlinePlayers = currentRooms.SelectMany(room => room.Players.Values).ToList();
        foreach (var user in Licenses.Users)
        {
            user.IsOnline = onlinePlayers.Any(player => player.Fc == user.FriendCode);
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
            Licenses.Users.Add(CreateDummyUser());
        return Ok();
    }

    private static LicenseProfile CreateDummyUser()
    {
        var dummyUser = new LicenseProfile
        {
            FriendCode = "0000-0000-0000",
            Mii = new(),
            Vr = 5000,
            Br = 5000,
            TotalRaceCount = 0,
            TotalWinCount = 0,
            Friends = [],
            RegionId = 10, // 10 => “unknown”
            IsOnline = false,
        };
        return dummyUser;
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
                Licenses.Users.Add(CreateDummyUser());
                continue;
            }

            var user = ParseLicenseUser(rkpdOffset);
            if (user.IsFailure)
            {
                Licenses.Users.Add(CreateDummyUser());
                continue;
            }
            Licenses.Users.Add(user.Value);
        }

        // Keep this here so we always have 4 users if the code above were to be changed
        while (Licenses.Users.Count < 4)
        {
            Licenses.Users.Add(CreateDummyUser());
        }
        return Ok();
    }

    private OperationResult<LicenseProfile> ParseLicenseUser(int rkpdOffset)
    {
        if (_rksysData == null)
            return new ArgumentNullException(nameof(_rksysData));

        var friendCode = FriendCodeGenerator.GetFriendCode(_rksysData, rkpdOffset + 0x5C);
        var miiDataResult = ParseMiiData(rkpdOffset);
        var miiToUse = miiDataResult.IsFailure ? new() : miiDataResult.Value;
        var user = new LicenseProfile
        {
            Mii = miiToUse,
            FriendCode = friendCode,
            Vr = BigEndianBinaryReader.ReadUint16(_rksysData, rkpdOffset + 0xB0),
            Br = BigEndianBinaryReader.ReadUint16(_rksysData, rkpdOffset + 0xB2),
            TotalRaceCount = BigEndianBinaryReader.ReadUint32(_rksysData, rkpdOffset + 0xB4),
            TotalWinCount = BigEndianBinaryReader.ReadUint32(_rksysData, rkpdOffset + 0xDC),
            BadgeVariants = _whWzDataSingletonService.GetBadges(friendCode),
            // Region is often found near offset 0x23308 + 0x3802 in RKGD. This code is a partial guess.
            // In practice, region might be read differently depending on your rksys layout.
            RegionId = (uint)BigEndianBinaryReader.ReadUint16(_rksysData, 0x23308 + 0x3802) / 4096,
        };

        ParseFriends(user, rkpdOffset);
        return user;
    }

    private OperationResult<Mii> ParseMiiData(int rkpdOffset)
    {
        //https://wiki.tockdom.com/wiki/Rksys.dat#DWC_User_Data
        if (_rksysData == null)
            return new ArgumentNullException(nameof(_rksysData));

        // licenseName is NOT always the same as mii name, could be useful
        var licenseName = BigEndianBinaryReader.GetUtf16String(_rksysData, rkpdOffset + 0x14, 10);
        // id of mii
        var avatarId = BigEndianBinaryReader.ReadUint32(_rksysData, rkpdOffset + 0x28);
        // id of the actual system
        var clientId = BigEndianBinaryReader.ReadUint32(_rksysData, rkpdOffset + 0x2C);

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

            byte[] rawMiiBytes = _rksysData.AsSpan(currentOffset + 0x1A, MiiSize).ToArray();
            var friendCode = FriendCodeGenerator.GetFriendCode(_rksysData, currentOffset + 4);
            var miiResult = MiiSerializer.Deserialize(rawMiiBytes);
            if (miiResult.IsFailure)
                continue;

            var friend = new FriendProfile
            {
                Vr = BigEndianBinaryReader.ReadUint16(_rksysData, currentOffset + 0x16),
                Br = BigEndianBinaryReader.ReadUint16(_rksysData, currentOffset + 0x18),
                FriendCode = friendCode,
                Wins = BigEndianBinaryReader.ReadUint16(_rksysData, currentOffset + 0x14),
                Losses = BigEndianBinaryReader.ReadUint16(_rksysData, currentOffset + 0x12),
                CountryCode = _rksysData[currentOffset + 0x68],
                RegionId = _rksysData[currentOffset + 0x69],
                BadgeVariants = _whWzDataSingletonService.GetBadges(friendCode),
                Mii = miiResult.Value,
            };
            licenseProfile.Friends.Add(friend);
        }
    }

    public OperationResult ChangeMii(int userIndex, Mii? newMii)
    {
        if (newMii is null)
            return "Mii cannot be null.";
        if (userIndex is < 0 or >= MaxPlayerNum)
            return "Invalid license index. Please select a valid license.";

        var serialised = MiiSerializer.Serialize(newMii);
        if (serialised.IsFailure)
            return serialised.Error!.Message;

        var existing = _miiService.GetByAvatarId(newMii.MiiId);
        if (existing.IsFailure)
            return existing.Error!.Message;

        var licence = Licenses.Users[userIndex];
        licence.Mii = newMii;

        if (_rksysData is null || _rksysData.Length < RksysSize)
            return "Invalid or unloaded rksys.dat data.";

        var rkpdOffset = 0x08 + userIndex * RkpdSize;
        BigEndianBinaryReader.WriteUInt32(_rksysData, rkpdOffset + 0x28, newMii.MiiId); // Avatar ID

        var systemid = newMii.SystemId0 << 24 | newMii.SystemId1 << 16 | newMii.SystemId2 << 8 | newMii.SystemId3;

        BigEndianBinaryReader.WriteUInt32(_rksysData, rkpdOffset + 0x2C, (uint)systemid);

        var nameWrite = WriteLicenseNameToSaveData(userIndex, newMii.Name.ToString());
        if (nameWrite.IsFailure)
            return nameWrite.Error!.Message;

        var saveResult = SaveRksysToFile();
        if (saveResult.IsFailure)
            return saveResult.Error!.Message;

        return Ok();
    }

    private bool CheckForMiiData(int offset)
    {
        // If the entire 0x4A bytes are zero, we treat it as empty / no Mii data
        for (var i = 0; i < MiiSize; i++)
        {
            if (_rksysData != null && _rksysData[offset + i] != 0)
                return true;
        }

        return false;
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
                return "Save folder not found";

            var currentRegion = (MarioKartWiiEnums.Regions)SettingsManager.RR_REGION.Get();
            if (currentRegion == MarioKartWiiEnums.Regions.None)
            {
                // Double check if there's at least one valid region
                var validRegions = RRRegionManager.GetValidRegions();
                if (validRegions.First() != MarioKartWiiEnums.Regions.None)
                {
                    currentRegion = validRegions.First();
                    SettingsManager.RR_REGION.Set(currentRegion);
                }
                else
                {
                    return "No valid regions found";
                }
            }

            var saveFileFolder = _fileSystem.Path.Combine(PathManager.SaveFolderPath, RRRegionManager.ConvertRegionToGameId(currentRegion));
            var saveFile = _fileSystem.Directory.GetFiles(saveFileFolder, "rksys.dat", SearchOption.TopDirectoryOnly);
            if (saveFile.Length == 0)
                return "rksys.dat not found";
            return _fileSystem.File.ReadAllBytes(saveFile[0]);
        }
        catch
        {
            return "Failed to load rksys.dat";
        }
    }

    /// <summary>
    /// Calculates the CRC32 of the specified slice of bytes using the
    /// standard polynomial (0xEDB88320) in the same way MKWii does.
    /// </summary>
    public static uint ComputeCrc32(byte[] data, int offset, int length)
    {
        const uint POLY = 0xEDB88320;
        var crc = 0xFFFFFFFF;

        for (var i = offset; i < offset + length; i++)
        {
            var b = data[i];
            crc ^= b;
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ POLY;
                else
                    crc >>= 1;
            }
        }

        return ~crc;
    }

    /// <summary>
    /// Fixes the MKWii save file by recalculating and inserting the CRC32 at 0x27FFC.
    /// </summary>
    public static void FixRksysCrc(byte[] rksysData)
    {
        if (rksysData == null || rksysData.Length < RksysSize)
            throw new ArgumentException("Invalid rksys.dat data");

        var lengthToCrc = 0x27FFC;
        var newCrc = ComputeCrc32(rksysData, 0, lengthToCrc);

        // 2) Write CRC at offset 0x27FFC in big-endian.
        BigEndianBinaryReader.WriteUInt32(rksysData, 0x27FFC, newCrc);
    }

    public OperationResult ChangeMiiName(int userIndex, string? newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
            return "Cannot set name to an empty name.";
        if (userIndex is < 0 or >= MaxPlayerNum)
            return "Invalid license index. Please select a valid license.";

        var user = Licenses.Users[userIndex];
        var miiIsEmptyOrNoName = IsNoNameOrEmptyMii(user);

        if (miiIsEmptyOrNoName)
            return "This license has no Mii data or is incomplete.\n" + "Please use the Mii Channel to create a Mii first.";

        if (user.Mii == null)
            return "This license has no Mii data or is incomplete.\n" + "Please use the Mii Channel to create a Mii first.";

        newName = Regex.Replace(newName, @"\s+", " ");

        // Basic checks
        if (newName.Length is > 10 or < 3)
            return "Names must be between 3 and 10 characters long.";

        if (newName.Length > 10)
            newName = newName.Substring(0, 10);
        var nameResult = MiiName.Create(newName);
        if (nameResult.IsFailure)
            return nameResult.Error.Message;

        user.Mii.Name = nameResult.Value;
        var nameWrite = WriteLicenseNameToSaveData(userIndex, newName);
        if (nameWrite.IsFailure)
            return nameWrite.Error.Message;
        var updated = _miiService.UpdateName(user.Mii.MiiId, newName);
        if (updated.IsFailure)
            return updated.Error.Message;
        var rksysSaveResult = SaveRksysToFile();
        if (rksysSaveResult.IsFailure)
            return rksysSaveResult.Error.Message;

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
            return "Invalid save data";
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
        if (_rksysData == null || !SettingsHelper.PathsSetupCorrectly())
            return Fail("Invalid save data or config is not setup properly.");
        FixRksysCrc(_rksysData);
        var currentRegion = (MarioKartWiiEnums.Regions)SettingsManager.RR_REGION.Get();
        var saveFolder = _fileSystem.Path.Combine(PathManager.SaveFolderPath, RRRegionManager.ConvertRegionToGameId(currentRegion));
        var trySaveRksys = TryCatch(() =>
        {
            _fileSystem.Directory.CreateDirectory(saveFolder);
            var path = _fileSystem.Path.Combine(saveFolder, "rksys.dat");
            _fileSystem.File.WriteAllBytes(path, _rksysData);
        });
        if (trySaveRksys.IsFailure)
            return trySaveRksys.Error.Message;
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
