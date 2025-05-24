using System.IO.Abstractions;
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

    /// <summary>
    /// Forcefully creates a new database file.
    /// </summary>
    /// <returns></returns>
    OperationResult ForceCreateDatabase();
}

public class MiiRepositoryServiceService(IFileSystem fileSystem) : IMiiRepositoryService
{
    private readonly IFileSystem _fileSystem;
    private const int MiiLength = 74;
    private const int MaxMiiSlots = 100;
    private const int CrcOffset = 0x1F1DE;
    private const int HeaderOffset = 0x04;
    private static readonly byte[] EmptyMii = Enumerable.Repeat((byte)0x00, MiiLength).ToArray();
    private string _miiDbFilePath => PathManager.MiiDbFile;

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
        if (!fileSystem.File.Exists(_miiDbFilePath))
            return "RFL_DB.dat not found.";

        var db = ReadDatabase();
        if (db.Length >= CrcOffset + 2)
        {
            // compute CRC over everything before CrcOffset
            var existingCrc = (ushort)((db[CrcOffset] << 8) | db[CrcOffset + 1]);
            var calcCrc = CalculateCrc16(db, 0, CrcOffset);

            if (existingCrc != calcCrc)
                return Fail($"Corrupt Mii database (bad CRC 0x{existingCrc:X4}, expected 0x{calcCrc:X4}).");
        }

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

        fileSystem.File.WriteAllBytes(_miiDbFilePath, db);
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

    public bool Exists() => fileSystem.File.Exists(_miiDbFilePath);

    public OperationResult ForceCreateDatabase()
    {
        if (fileSystem.File.Exists(_miiDbFilePath))
            return "Database already exists.";

        var directory = Path.GetDirectoryName(_miiDbFilePath);
        if (!string.IsNullOrEmpty(directory) && !fileSystem.Directory.Exists(directory))
        {
            fileSystem.Directory.CreateDirectory(directory);
        }

        var db = new byte[779_968];
        // first 4 bytes should be the RNOD magic "RNOD"
        db[0] = 0x52;
        db[1] = 0x4E;
        db[2] = 0x4F;
        db[3] = 0x44;

        db[0x1CE0 + 0x0C] = 0x80;
        //and at offset 0x01d00 we have the RNHD magic "RNHD"
        db[0x1D00] = 0x52;
        db[0x1D01] = 0x4E;
        db[0x1D02] = 0x48;
        db[0x1D03] = 0x44;

        db[0x1D04] = 0xFF;
        db[0x1D05] = 0xFF;
        db[0x1D06] = 0xFF;
        db[0x1D07] = 0xFF;

        var crc = CalculateCrc16(db, 0, CrcOffset);
        db[CrcOffset] = (byte)(crc >> 8);
        db[CrcOffset + 1] = (byte)(crc & 0xFF);
        fileSystem.File.WriteAllBytes(_miiDbFilePath, db);

        return Ok();
    }

    public OperationResult UpdateBlockByClientId(uint clientId, byte[] newBlock)
    {
        if (clientId == 0)
            return "Invalid ClientId.";
        if (newBlock.Length != MiiLength)
            return "Mii block size invalid.";
        if (!fileSystem.File.Exists(_miiDbFilePath))
            return "RFL_DB.dat not found.";

        var allBlocks = LoadAllBlocks();
        var updated = false;

        foreach (var t in allBlocks)
        {
            var block = t;
            if (block.Length != MiiLength)
                continue;

            var thisId = BigEndianBinaryReader.BufferToUint32(block, 0x18);
            if (thisId != clientId)
                continue;

            Array.Copy(newBlock, 0, t, 0, MiiLength);
            updated = true;
            break;
        }

        return !updated ? Fail("Mii not found.") : SaveAllBlocks(allBlocks);
    }

    private byte[] ReadDatabase()
    {
        try
        {
            return Exists() ? fileSystem.File.ReadAllBytes(_miiDbFilePath) : [];
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

        return !inserted ? "No empty Mii slot available." : SaveAllBlocks(blocks);
    }
}
