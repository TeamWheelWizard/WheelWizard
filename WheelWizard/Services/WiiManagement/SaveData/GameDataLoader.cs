using System.Text;
using System.Text.RegularExpressions;
using WheelWizard.Models.Enums;
using WheelWizard.Models.GameData;
using WheelWizard.Models.MiiImages;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.Other;
using WheelWizard.Services.Settings;
using WheelWizard.Utilities.Generators;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WheelWizardData;

// big big thanks to https://kazuki-4ys.github.io/web_apps/FaceThief/ for the JS implementation

namespace WheelWizard.Services.WiiManagement.SaveData;

public class GameDataLoader : RepeatedTaskManager
{
    public static GameDataLoader Instance { get; } = new();

    /// <summary>
    /// The path to where the “rksys.dat” folder structure is expected to live, e.g.
    ///   ..\path\to\Riivolution\riivolution\save\RetroWFC\RMCP\rksys.dat
    /// </summary>
    private static string? TryCreateSaveFolderPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(PathManager.UserFolderPath))
                return string.Empty;
            if (Directory.Exists(PathManager.SaveFolderPath)) 
                return PathManager.SaveFolderPath;
            try
            {
                Directory.CreateDirectory(PathManager.SaveFolderPath);
            }
            catch (Exception ex)
            {
                // Do nothing until user directory is resolved.
                return string.Empty;
            }
            return PathManager.SaveFolderPath;
        }
    }

    private byte[]? _saveData;

    private GameData GameData { get; }

    private const int RksysSize   = 0x2BC000;
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
    public GameDataUser GetCurrentUser 
        => Instance.GameData.Users[(int)SettingsManager.FOCUSSED_USER.Get()];

    public List<GameDataFriend> GetCurrentFriends 
        => Instance.GameData.Users[(int)SettingsManager.FOCUSSED_USER.Get()].Friends;

    public GameData GetGameData 
        => Instance.GameData;

    public GameDataUser GetUserData(int index) 
        => GameData.Users[index];

    public bool HasAnyValidUsers 
        => GameData.Users.Any(user => user.FriendCode != "0000-0000-0000");

    private GameDataLoader() : base(40)
    {
        GameData = new GameData();
        LoadGameData();
    }

    /// <summary>
    /// Refresh the "IsOnline" status of our local users based on the list of currently online players.
    /// </summary>
    public void RefreshOnlineStatus()
    {
        var currentRooms = RRLiveRooms.Instance.CurrentRooms;
        var onlinePlayers = currentRooms.SelectMany(room => room.Players.Values).ToList();
        foreach (var user in GameData.Users)
        {
            user.IsOnline = onlinePlayers.Any(player => player.Fc == user.FriendCode);
        }
    }

    /// <summary>
    /// Loads the entire rksys.dat file from disk into memory and parses the 4 possible licenses.
    /// If the file is invalid or not found, we create dummy users.
    /// </summary>
    public void LoadGameData()
    {
        try
        {
            _saveData = LoadSaveDataFile();
            if (_saveData != null && ValidateMagicNumber())
            {
                ParseUsers();
                return;
            }

            // If the file was invalid or not found, create 4 dummy licenses
            GameData.Users.Clear();
            for (var i = 0; i < MaxPlayerNum; i++)
                CreateDummyUser();
            
        }
        catch (Exception e)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Loading game data failed")
                .SetInfoText($"An error occurred while loading the game data: {e.Message}")
                .Show();
        }
    }

    private void CreateDummyUser()
    {
        var dummyUser = new GameDataUser
        {
            FriendCode = "0000-0000-0000",
            MiiData = new MiiData
            {
                Mii = new Mii
                {
                    Name = "no license",
                    Data = Convert.ToBase64String(new byte[MiiSize])
                },
                AvatarId = 0,
                ClientId = 0
            },
            Vr = 5000,
            Br = 5000,
            TotalRaceCount = 0,
            TotalWinCount = 0,
            Friends = new List<GameDataFriend>(),
            RegionId = 10, // 10 => “unknown”
            IsOnline = false
        };
        GameData.Users.Add(dummyUser);
    }

    private void ParseUsers()
    {
        GameData.Users.Clear();
        if (_saveData == null) return;

        for (var i = 0; i < MaxPlayerNum; i++)
        {
            var rkpdOffset = RksysMagic.Length + i * RkpdSize;
            if (Encoding.ASCII.GetString(_saveData, rkpdOffset, RkpdMagic.Length) == RkpdMagic)
            {
                var user = ParseUser(rkpdOffset);
                GameData.Users.Add(user);
            }
            else
            {
                CreateDummyUser();
            }
        }
        if (GameData.Users.Count == 0)
            CreateDummyUser();
    }

    private GameDataUser ParseUser(int offset)
    {
        if (_saveData == null) throw new ArgumentNullException(nameof(_saveData));

        var friendCode = FriendCodeGenerator.GetFriendCode(_saveData, offset + 0x5C);
        var user = new GameDataUser
        {
            MiiData     = ParseMiiData(offset + 0x14),
            FriendCode  = friendCode,
            Vr          = BigEndianBinaryReader.BufferToUint16(_saveData, offset + 0xB0),
            Br          = BigEndianBinaryReader.BufferToUint16(_saveData, offset + 0xB2),
            TotalRaceCount = BigEndianBinaryReader.BufferToUint32(_saveData, offset + 0xB4),
            TotalWinCount   = BigEndianBinaryReader.BufferToUint32(_saveData, offset + 0xDC),
            BadgeVariants = App.Services.GetRequiredService<IWhWzDataSingletonService>().GetBadges(friendCode),
            // Region is often found near offset 0x23308 + 0x3802 in RKGD. This code is a partial guess.
            // In practice, region might be read differently depending on your rksys layout.
            RegionId = BigEndianBinaryReader.BufferToUint16(_saveData, 0x23308 + 0x3802) / 4096,
        };

        ParseFriends(user, offset);
        return user;
    }

    private MiiData ParseMiiData(int offset)
    {
        if (_saveData == null) throw new ArgumentNullException(nameof(_saveData));

        // In Mario Kart Wii's rksys, offset +0x10 => AvatarId, offset +0x14 => ClientId
        // The name is big-endian UTF-16 at offset itself (length 10 chars => 20 bytes).
        var name = BigEndianBinaryReader.GetUtf16String(_saveData, offset, 10);
        var avatarId = BitConverter.ToUInt32(_saveData, offset + 0x10);
        var clientId = BitConverter.ToUInt32(_saveData, offset + 0x14);

        // Convert the Mii block from RFL_DB if it’s actually valid
        var rawMii = InternalMiiManager.GetMiiDataByClientId(clientId);

        var miiData = new MiiData
        {
            Mii = new Mii
            {
                Name = name,
                Data = Convert.ToBase64String(rawMii)
            },
            AvatarId = avatarId,
            ClientId = clientId
        };
        return miiData;
    }

    private void ParseFriends(GameDataUser gameDataUser, int userOffset)
    {
        if (_saveData == null) return;

        var friendOffset = userOffset + FriendDataOffset;
        for (var i = 0; i < MaxFriendNum; i++)
        {
            var currentOffset = friendOffset + i * FriendDataSize;
            if (!CheckMiiData(currentOffset + 0x1A)) continue;

            var friendCode = FriendCodeGenerator.GetFriendCode(_saveData, currentOffset + 4);
            var friend = new GameDataFriend
            {
                Vr          = BigEndianBinaryReader.BufferToUint16(_saveData, currentOffset + 0x16),
                Br          = BigEndianBinaryReader.BufferToUint16(_saveData, currentOffset + 0x18),
                FriendCode  = friendCode,
                Wins        = BigEndianBinaryReader.BufferToUint16(_saveData, currentOffset + 0x14),
                Losses      = BigEndianBinaryReader.BufferToUint16(_saveData, currentOffset + 0x12),
                CountryCode = _saveData[currentOffset + 0x68],
                RegionId    = _saveData[currentOffset + 0x69],
                BadgeVariants = App.Services.GetRequiredService<IWhWzDataSingletonService>().GetBadges(friendCode),

                MiiData = new MiiData
                {
                    Mii = new Mii
                    {
                        Name = BigEndianBinaryReader.GetUtf16String(_saveData, currentOffset + 0x1C, 10),
                        Data = Convert.ToBase64String(_saveData.AsSpan(currentOffset + 0x1A, MiiSize))
                    },
                    AvatarId = 0,
                    ClientId = 0
                },
            };
            gameDataUser.Friends.Add(friend);
        }
    }

    private bool CheckMiiData(int offset)
    {
        // If the entire 0x4A bytes are zero, we treat it as empty / no Mii data
        for (var i = 0; i < MiiSize; i++)
        {
            if (_saveData != null && _saveData[offset + i] != 0)
                return true;
        }
        return false;
    }

    private bool ValidateMagicNumber()
    {
        if (_saveData == null) return false;
        return Encoding.ASCII.GetString(_saveData, 0, RksysMagic.Length) == RksysMagic;
    }

    private static byte[]? LoadSaveDataFile()
    {
        try
        {
            if (!Directory.Exists(TryCreateSaveFolderPath))
                return null;

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
                    return null;
                }
            }

            var saveFileFolder = Path.Combine(TryCreateSaveFolderPath, RRRegionManager.ConvertRegionToGameId(currentRegion));
            var saveFile = Directory.GetFiles(saveFileFolder, "rksys.dat", SearchOption.TopDirectoryOnly);
            return saveFile.Length == 0 ? null : File.ReadAllBytes(saveFile[0]);
        }
        catch
        {
            // If anything fails, return null
            return null;
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
        BigEndianBinaryReader.WriteUInt32BigEndian(rksysData, 0x27FFC, newCrc);
    }
    public async void PromptLicenseNameChange(int userIndex)
    {
        if (userIndex is < 0 or >= MaxPlayerNum)
        {
            InvalidLicenseMessage("Invalid license index. Please select a valid license.");
            return;
        }
        var user = GameData.Users[userIndex];
        var miiIsEmptyOrNoName = IsNoNameOrEmptyMii(user);

        if (miiIsEmptyOrNoName)
        {
            InvalidLicenseMessage("This license has no Mii data or is incomplete.\n" +
                                  "Please use the Mii Channel to create a Mii first.");
            return;
        }
        if (user.MiiData?.Mii == null)
        {
            InvalidLicenseMessage("This license has no Mii data or is incomplete.\n" +
                                  "Please use the Mii Channel to create a Mii first.");
            return;
        }
        var currentName = user.MiiData.Mii.Name ?? "";
        var renamePopup =  new TextInputWindow()
                .SetMainText($"Enter new name")
                .SetExtraText($"Changing name from: {currentName}")
                .SetAllowCustomChars(true)
                .SetInitialText(currentName)
                .SetPlaceholderText(currentName);

        var newName = await renamePopup.ShowDialog();
        if (string.IsNullOrWhiteSpace(newName)) return;
        newName = Regex.Replace(newName, @"\s+", " ");
        
        // Basic checks
        if (newName.Length is > 10 or < 3)
        {
            InvalidNameMessage("Names must be between 3 and 10 characters long.");
            return;
        }

        if (newName.Length > 10)
            newName = newName.Substring(0, 10);
        user.MiiData.Mii.Name = newName; // This should be updated just in case someone uses it, but its not the one that updates the profile page
        user.MiiName = newName; // This is the one with the notification
        WriteLicenseNameToSaveData(userIndex, newName);
        var updated = InternalMiiManager.UpdateMiiName(user.MiiData.ClientId, newName);
        if (!updated)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetTitleText("Failed to update the Mii name.")
                .SetInfoText("It was unable to update the name in the Mii Database file.")
                .Show();
          
        }

        if (SaveRksysToFile())
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Message)
                .SetTitleText("Successfully updated name")
                .SetInfoText($"Successfully updated Mii name to {user.MiiData.Mii.Name}")
                .Show();
        }
    }
    private bool IsNoNameOrEmptyMii(GameDataUser user)
    {
        if (user?.MiiData?.Mii == null)
            return true;

        var name = user.MiiData.Mii.Name?.Trim() ?? "";
        if (name.Equals("no name", StringComparison.OrdinalIgnoreCase))
            return true;
        var raw = Convert.FromBase64String(user.MiiData.Mii.Data ?? "");
        if (raw.Length != 74) return true; // Not valid
        if (raw.All(b => b == 0)) return true;

        // Otherwise, it’s presumably valid
        return false;
    }
    private void WriteLicenseNameToSaveData(int userIndex, string newName)
    {
        if (_saveData == null || _saveData.Length < RksysSize) return;
        var rkpdOffset = 0x8 + userIndex * RkpdSize; 
        var nameOffset = rkpdOffset + 0x14;
        var nameBytes = Encoding.BigEndianUnicode.GetBytes(newName);
        for (var i = 0; i < 20; i++)
            _saveData[nameOffset + i] = 0;
        Array.Copy(nameBytes, 0, _saveData, nameOffset, Math.Min(nameBytes.Length, 20));
    }
    
    private bool SaveRksysToFile()
    {
        if (_saveData == null || string.IsNullOrWhiteSpace(TryCreateSaveFolderPath)) return false;
        FixRksysCrc(_saveData);
        var currentRegion = (MarioKartWiiEnums.Regions)SettingsManager.RR_REGION.Get();
        var saveFolder = Path.Combine(TryCreateSaveFolderPath, RRRegionManager.ConvertRegionToGameId(currentRegion));

        try
        {
            Directory.CreateDirectory(saveFolder);
            var path = Path.Combine(saveFolder, "rksys.dat");
            File.WriteAllBytes(path, _saveData);
        }
        catch (Exception ex)
        {
            new MessageBoxWindow()
                .SetMessageType(MessageBoxWindow.MessageType.Error)
                .SetInfoText($"Failed to save rksys.dat.\n{ex.Message}")
                .SetTitleText("Failed to save the 'save' file")
                .Show();
            return false;
        }

        return true;
    }

    private void InvalidLicenseMessage(string info)
    {
        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .SetTitleText("Invalid license.")
            .SetInfoText(info)
            .Show();
    }
    
    private void InvalidNameMessage(string info)
    {
        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .SetTitleText("Invalid Name.")
            .SetInfoText(info)
            .Show();
    }
    
    protected override Task ExecuteTaskAsync()
    {
        LoadGameData();
        return Task.CompletedTask;
    }
}
