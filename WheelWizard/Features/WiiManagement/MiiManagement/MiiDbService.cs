using Testably.Abstractions;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.MiiManagement;

/// <summary>
/// Provides high-level operations for managing Miis in the Wii Mii database.
/// </summary>
public interface IMiiDbService
{
    /// <summary>
    /// Retrieves all Miis stored in the database.
    /// </summary>
    /// <returns>A list of fully deserialized <see cref="Mii"/> instances.</returns>
    List<Mii> GetAllMiis();

    /// <summary>
    /// Retrieves a specific Mii from the database using its unique client ID.
    /// </summary>
    /// <param name="avatarId">The unique identifier of the Mii to retrieve.</param>
    /// <returns>An <see cref="OperationResult{T}"/> containing the <see cref="Mii"/> if found and valid.</returns>
    OperationResult<Mii> GetByAvatarId(uint avatarId);

    /// <summary>
    /// Updates an existing Mii in the database with new data.
    /// </summary>
    /// <param name="updatedMii">The updated <see cref="Mii"/> object to store.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    OperationResult Update(Mii updatedMii);

    /// <summary>
    /// Updates the name of an existing Mii in the database.
    /// </summary>
    /// <param name="clientId">The unique identifier of the Mii to update.</param>
    /// <param name="newName">The new name to assign to the Mii.</param>
    /// <returns>An <see cref="OperationResult"/> indicating success or failure.</returns>
    OperationResult UpdateName(uint clientId, string newName);

    /// <summary>
    /// Adds a new Mii to the database.
    /// </summary>
    /// <param name="newMii"></param>
    /// <param name="macAddress"></param>
    OperationResult AddToDatabase(Mii? newMii, string macAddress);

    /// <summary>
    /// Removes (deletes) the Mii with the given client ID by zeroing out its slot.
    /// </summary>
    OperationResult Remove(uint clientId);

    /// <summary>
    /// Whether the database exists or not.
    /// </summary>
    bool Exists();
}

public class MiiDbService(IMiiRepositoryService repository, IRandomSystem randomSystem) : IMiiDbService
{
    public List<Mii> GetAllMiis()
    {
        var result = new List<Mii>();
        var blocks = repository.LoadAllBlocks();

        foreach (var block in blocks)
        {
            var miiResult = MiiSerializer.Deserialize(block);
            if (miiResult.IsSuccess)
                result.Add(miiResult.Value);
        }

        return result;
    }

    public bool Exists() => repository.Exists();

    public OperationResult<Mii> GetByAvatarId(uint avatarId)
    {
        var raw = repository.GetRawBlockByAvatarId(avatarId);
        if (raw == null)
            return Fail("Mii block not found", MessageTranslation.Error_UpdateMiiDb_NoBlockFound);
        if (raw.Length != MiiSerializer.MiiBlockSize)
            return Fail("Mii block size invalid.", MessageTranslation.Error_UpdateMiiDb_BlockSizeInvalid);

        return MiiSerializer.Deserialize(raw);
    }

    public OperationResult Update(Mii updatedMii)
    {
        var serialized = MiiSerializer.Serialize(updatedMii);
        if (serialized.IsFailure)
            return serialized;
        var value = repository.UpdateBlockByClientId(updatedMii.MiiId, serialized.Value);
        return value;
    }

    public OperationResult UpdateName(uint clientId, string newName)
    {
        var result = GetByAvatarId(clientId);
        if (result.IsFailure)
            return result;

        var mii = result.Value;

        var nameResult = MiiName.Create(newName);
        if (nameResult.IsFailure)
            return nameResult;

        mii.Name = nameResult.Value;
        return Update(mii);
    }

    public OperationResult AddToDatabase(Mii? newMii, string macAddress)
    {
        if (newMii == null)
            return Fail("Mii cannot be null", MessageTranslation.Error_MiiSerializer_MiiNotNull);

        var macParts = macAddress.Split(':');
        if (macParts.Length != 6)
            return Fail("Invalid MAC address format.", MessageTranslation.Error_UpdateMiiDb_InvalidMac);
        newMii.IsInvalid = false;

        var getMacAddress = TryCatch(() =>
        {
            var macBytes = new byte[6];
            for (var i = 0; i < 6; i++)
                macBytes[i] = byte.Parse(macParts[i], System.Globalization.NumberStyles.HexNumber);
            newMii.SystemId0 = (byte)((macBytes[0] + macBytes[1] + macBytes[2]) & 0xFF);
            newMii.SystemId1 = macBytes[3];
            newMii.SystemId2 = macBytes[4];
            newMii.SystemId3 = macBytes[5];
        });
        if (getMacAddress.IsFailure)
            return getMacAddress;

        var miiId = GenerateMiiId();
        newMii.MiiId1 = miiId[0];
        newMii.MiiId2 = miiId[1];
        newMii.MiiId3 = miiId[2];
        newMii.MiiId4 = miiId[3];

        var serialized = MiiSerializer.Serialize(newMii);

        if (serialized.IsFailure)
            return serialized;

        var result = repository.AddMiiToBlocks(serialized.Value);
        return result;
    }

    private static readonly object _miiIdLock = new();
    private static uint _lastCounter;
    private static uint _sequenceOffset;

    // This took me days to figure out :))))
    // The Mii ID is a 32-bit unsigned integer,
    // where the first 3 bits are used to indicate the type of Mii i think
    // The remaining 29 bits are a counter that increments every 4 seconds from a fixed epoch (January 1, 2006).
    // For our implementation the counter is incremented by a sequence offset to ensure uniqueness even if the function is called multiple times in the same tick.
    private static byte[] GenerateMiiId(bool isBlue = false)
    {
        // Epoch for Wii: January 1, 2006 UTC
        var epoch = new DateTime(2006, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Current time in UTC
        var now = DateTime.UtcNow;

        // Compute base tick (4‑second resolution)
        var baseCounter = (uint)((now - epoch).TotalSeconds / 4u);

        uint actualCounter;
        lock (_miiIdLock)
        {
            if (baseCounter == _lastCounter)
            {
                // same tick as last time: bump the offset
                _sequenceOffset++;
            }
            else
            {
                // new tick: reset offset
                _lastCounter = baseCounter;
                _sequenceOffset = 0;
            }

            actualCounter = baseCounter + _sequenceOffset;
        }

        var prefixBits = isBlue ? 0b110u : 0b100u;
        var miiId =
            (prefixBits << 29) // top 3 bits
            | (actualCounter & 0x1FFFFFFFu); // lower 29 bits

        return [(byte)(miiId >> 24), (byte)(miiId >> 16), (byte)(miiId >> 8), (byte)(miiId)];
    }

    public OperationResult Remove(uint clientId)
    {
        if (clientId == 0)
            return Fail("Invalid Client ID.", MessageTranslation.Error_UpdateMiiDb_InvalidClId);
        var emptyBlock = new byte[74];
        return repository.UpdateBlockByClientId(clientId, emptyBlock);
    }

    /* TODO: Find out if we wanna keep this
    public uint GenerateCustomMiiId()
    {
        var rng = randomSystem.Random.Shared;

        // Byte 0: ensure high bit = 1 (so ID ≥ 0x80000000)
        var b0 = (byte)(rng.Next(0, 0x40) | 0x80);

        // Bytes 1–3: fully random
        var b1 = (byte)rng.Next(0, 0x100);
        var b2 = (byte)rng.Next(0, 0x100);
        var b3 = (byte)rng.Next(0, 0x100);

        // Combine into big‑endian uint:
        return ((uint)b0 << 24) | ((uint)b1 << 16) | ((uint)b2 << 8) | (uint)b3;
    }
    */
}
