﻿using System.IO.Abstractions;
using WheelWizard.Services;
using WheelWizard.Services.WiiManagement.SaveData;

namespace WheelWizard.WiiManagement;

public interface IMiiRepositoryService
{
    /// <summary>
    /// Loads all 100 Mii data blocks from the Wii Mii database
    /// Returns a list of byte arrays, each representing a Mii block.
    /// </summary>
    List<byte[]> LoadAllBlocks();

    /// <summary>
    /// Saves all Mii data blocks to the Wii Mii database.
    /// Automatically Pads to 100 entries and calculates CRC.
    /// </summary>
    /// <param name="blocks">List of a raw 74 Byte-array Representing a Mii.</param>
    OperationResult SaveAllBlocks(List<byte[]> blocks);

    /// <summary>
    /// Retrieves a raw Mii block by its unique client ID.
    /// returns null if the Mii is not found.
    /// </summary>
    /// <param name="clientId">The Mii's unique client Id</param>
    byte[]? GetRawBlockByAvatarId(uint clientId);

    /// <summary>
    /// Replaces a Mii block in the database that matches the given ID.
    /// </summary>
    /// <param name="clientId">The unique ID of the mii to search for</param>
    /// <param name="newBlock">the new raw Mii data</param>
    OperationResult UpdateBlockByClientId(uint clientId, byte[] newBlock);

    /// <summary>
    /// Adds a new Mii block to the database.
    /// </summary>
    /// <param name="rawMiiData"></param>
    OperationResult AddMiiToBlocks(byte[] rawMiiData);

    /// <summary>
    /// Whether the database file exists or not.
    /// </summary>
    bool Exists();
}

public class MiiRepositoryServiceService(IFileSystem fileSystem) : IMiiRepositoryService
{
    private const int MiiLength = 74;
    private const int MaxMiiSlots = 100;
    private const int CrcOffset = 0x1F1DE;
    private const int HeaderOffset = 0x04;
    private static readonly byte[] EmptyMii = Enumerable.Repeat((byte)0x00, MiiLength).ToArray();
    private readonly string _wiiDbFilePath = PathManager.WiiDbFile;

    public List<byte[]> LoadAllBlocks()
    {
        var result = new List<byte[]>();

        var database = ReadDatabase();
        if (database.Length < HeaderOffset)
            return result;

        using var ms = new MemoryStream(database);
        ms.Seek(HeaderOffset, SeekOrigin.Begin);

        for (var i = 0; i < MaxMiiSlots; i++)
        {
            var block = new byte[MiiLength];
            var read = ms.Read(block, 0, MiiLength);
            if (read < MiiLength)
                break;

            result.Add(block.SequenceEqual(EmptyMii) ? new byte[MiiLength] : block);
        }

        return result;
    }

    public OperationResult SaveAllBlocks(List<byte[]> blocks)
    {
        if (!fileSystem.File.Exists(_wiiDbFilePath))
            return "RFL_DB.dat not found.";

        var db = ReadDatabase();
        using var ms = new MemoryStream(db);
        ms.Seek(HeaderOffset, SeekOrigin.Begin);

        for (var i = 0; i < MaxMiiSlots; i++)
        {
            var block = i < blocks.Count ? blocks[i] : EmptyMii;
            ms.Write(block, 0, MiiLength);
        }

        if (db.Length >= CrcOffset + 2)
        {
            var crc = CalculateCrc16(db, 0, CrcOffset);
            db[CrcOffset] = (byte)(crc >> 8);
            db[CrcOffset + 1] = (byte)(crc & 0xFF);
        }

        fileSystem.File.WriteAllBytes(_wiiDbFilePath, db);
        return Ok();
    }

    public byte[]? GetRawBlockByAvatarId(uint clientId)
    {
        if (clientId == 0)
            return null;

        var blocks = LoadAllBlocks();
        foreach (var block in blocks)
        {
            if (block.Length != MiiLength)
                continue;

            var thisId = BigEndianBinaryReader.BufferToUint32(block, 0x18);
            if (thisId == clientId)
                return block;
        }

        return null;
    }

    public bool Exists() => fileSystem.File.Exists(_wiiDbFilePath);

    public OperationResult UpdateBlockByClientId(uint clientId, byte[] newBlock)
    {
        if (clientId == 0)
            return "Invalid ClientId.";
        if (newBlock.Length != MiiLength)
            return "Mii block size invalid.";
        if (!fileSystem.File.Exists(_wiiDbFilePath))
            return "RFL_DB.dat not found.";

        var allBlocks = LoadAllBlocks();
        var updated = false;

        for (int i = 0; i < allBlocks.Count; i++)
        {
            var block = allBlocks[i];
            if (block.Length != MiiLength)
                continue;

            var thisId = BigEndianBinaryReader.BufferToUint32(block, 0x18);
            if (thisId != clientId)
                continue;

            Array.Copy(newBlock, 0, allBlocks[i], 0, MiiLength);
            updated = true;
            break;
        }

        if (!updated)
            return Fail("Mii not found.");

        return SaveAllBlocks(allBlocks);
    }

    private byte[] ReadDatabase()
    {
        try
        {
            return Exists() ? fileSystem.File.ReadAllBytes(_wiiDbFilePath) : [];
        }
        catch
        {
            return [];
        }
    }

    private static ushort CalculateCrc16(byte[] buf, int off, int len)
    {
        const ushort poly = 0x1021;
        ushort crc = 0x0000;
        for (var i = off; i < off + len; i++)
        {
            crc ^= (ushort)(buf[i] << 8);
            for (var b = 0; b < 8; b++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ poly) : (ushort)(crc << 1);
        }
        return crc;
    }

    public OperationResult AddMiiToBlocks(byte[]? rawMiiData)
    {
        if (rawMiiData is not { Length: MiiLength })
            return "Invalid Mii block size.";

        // Load all 100 blocks.
        var blocks = LoadAllBlocks();
        var inserted = false;

        // Look for an empty slot.
        for (var i = 0; i < blocks.Count; i++)
        {
            if (!blocks[i].SequenceEqual(EmptyMii))
                continue;

            blocks[i] = rawMiiData;
            inserted = true;
            break;
        }

        if (!inserted)
            return "No empty Mii slot available.";

        // Save the updated blocks back to the database.
        return SaveAllBlocks(blocks);
    }
}
