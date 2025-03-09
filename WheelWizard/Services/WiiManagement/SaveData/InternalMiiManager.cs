using System.Text;

namespace WheelWizard.Services.WiiManagement.SaveData;

public static class InternalMiiManager
{
    private static readonly string WiiDbFile = PathManager.WiiDbFile;

    private const int MiiLength = 74; // Each Mii block is 74 bytes
    private static readonly byte[] emptyMii = new byte[MiiLength];

    /// <summary>
    /// Reads the entire RFL_DB.dat and returns up to 100 Mii blocks (74 bytes each).
    /// </summary>
    public static List<byte[]> GetAllMiiData()
    {
        var miis = new List<byte[]>();
        var allMiiData = GetMiiDb();
        if (allMiiData.Length < 4)
            return miis;

        using var memoryStream = new MemoryStream(allMiiData);
        
        // According to some docs, the first 4 bytes is a "Plaza Offset" or "RNCD" magic, skip them
        memoryStream.Seek(0x4, SeekOrigin.Begin);

        for (var i = 0; i < 100; i++)
        {
            var miiData = new byte[MiiLength];
            var bytesRead = memoryStream.Read(miiData, 0, MiiLength);
            if (bytesRead < MiiLength)
                break;
            miis.Add(miiData.SequenceEqual(emptyMii) ? new byte[MiiLength] : miiData);
        }
        return miis;
    }

    /// <summary>
    /// Overwrites the local RFL_DB.dat with the given list of Mii blocks.
    /// Keeps the initial 4 bytes intact, writes or clears up to 100 Mii slots,
    /// then recalculates the CRC16 at 0x1F1DE (if large enough).
    /// </summary>
    private static void SaveMiiDb(List<byte[]> allMiis)
    {
        if (!File.Exists(WiiDbFile))
            return;
        
        var dbFile = File.ReadAllBytes(WiiDbFile);
        using var ms = new MemoryStream(dbFile);
        ms.Seek(0x4, SeekOrigin.Begin);
        for (var i = 0; i < 100; i++)
        {
            var block = i < allMiis.Count ? allMiis[i] : emptyMii;
            ms.Write(block, 0, MiiLength);
        }
        const int crcOffset = 0x1F1DE;
        if (dbFile.Length >= crcOffset + 2)
        {
            var crc = CalculateCRC16(dbFile, 0, crcOffset);
            dbFile[crcOffset]     = (byte)(crc >> 8);
            dbFile[crcOffset + 1] = (byte)(crc & 0xFF);
        }
        File.WriteAllBytes(WiiDbFile, dbFile);
    }

    /// <summary>
    /// Retrieves the *raw* 74-byte Mii block that has the specified ClientId (found at offset 0x18..0x1B).
    /// Returns an empty array if none found or if RFL_DB.dat is missing.
    /// </summary>
    public static byte[] GetMiiDataByClientId(uint clientId)
    {
        if (clientId == 0) return Array.Empty<byte>();

        var allMiis = GetAllMiiData();
        foreach (var block in allMiis)
        {
            if (block.Length != MiiLength) 
                continue;

            var thisMiiId = BigEndianBinaryReader.ReadLittleEndianUInt32(block, 0x18);
            if (thisMiiId == clientId)
                return block;
        }
        return Array.Empty<byte>();
    }
    
    public static bool UpdateMiiName(uint clientId, string newName)
    {
        if (clientId == 0)
            return false;

        if (!File.Exists(WiiDbFile))
            return false;

        var allMiis = GetAllMiiData();
        var updated = false;

        for (var i = 0; i < allMiis.Count; i++)
        {
            var block = allMiis[i];
            if (block.Length != MiiLength)
                continue;

            var thisMiiId = BigEndianBinaryReader.ReadLittleEndianUInt32(block, 0x18);
            if (thisMiiId != clientId) continue;
            
            // Found the Mii
            WriteMiiName(block, newName);
            allMiis[i] = block;
            updated = true;
            break;
        }
        if (updated)
            SaveMiiDb(allMiis);
        return updated;
    }

    /// <summary>
    /// Writes a 10-character Mii name into the 74-byte Mii block at offset 0x02,
    /// Max length is 10 chars => 20 bytes.
    /// </summary>
    private static void WriteMiiName(byte[] miiBlock, string name)
    {
        if (name.Length > 10)
            name = name.Substring(0, 10);

        // Clear out the old name area
        for (var i = 0; i < 20; i++)
            miiBlock[2 + i] = 0;

        // Convert to big-endian UTF-16 (this is what Wii expects)
        var nameBytes = Encoding.BigEndianUnicode.GetBytes(name);
        Array.Copy(nameBytes, 0, miiBlock, 2, Math.Min(nameBytes.Length, 20));
    }
    
    private static byte[] GetMiiDb()
    {
        try
        {
            return !File.Exists(WiiDbFile) ? Array.Empty<byte>() : File.ReadAllBytes(WiiDbFile);
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }
    
    private static ushort CalculateCRC16(byte[] data, int offset, int length)
    {
        const ushort polynomial = 0x1021;
        ushort crc = 0x0000;

        for (var i = offset; i < offset + length; i++)
        {
            crc ^= (ushort)(data[i] << 8);
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ polynomial);
                else
                    crc <<= 1;
            }
        }
        return crc;
    }
}
