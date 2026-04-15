using WheelWizard.Models;
using System.Text;
using Serilog;

namespace WheelWizard.Services;

public class RkgParser
{
    public static LocalGhostData? ParseRkgFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                Log.Debug("RKG file does not exist: {FilePath}", filePath);
                return null;
            }

            var data = File.ReadAllBytes(filePath);
            Log.Debug("Reading RKG file: {FilePath}, Size: {Size} bytes", filePath, data.Length);
            
            if (data.Length < 0x88)
            {
                Log.Warning("RKG file too small ({Size} bytes, minimum 136): {FilePath}", data.Length, filePath);
                return null;
            }
            
            if (!IsValidRkgFile(data))
            {
                Log.Warning("Invalid RKG header in file: {FilePath}", filePath);
                return null;
            }

            var ghostData = new LocalGhostData
            {
                FilePath = filePath,
                TrackId = ParseTrackId(data),
                CharacterId = ParseCharacterId(data),
                VehicleId = ParseVehicleId(data),
                ControllerType = ParseControllerType(data),
                TotalTimeMs = ParseFinishTime(data),
                Date = ParseDate(data, filePath),
                LapSplitsMs = ParseLapSplits(data),
                Country = ParseCountryCode(data),
                MiiData = ParseMiiData(data)
            };
            
            Log.Debug("RKG Parse Results: TrackId={TrackId}, CharId={CharId}, VehicleId={VehicleId}, ControllerType={ControllerType}, Time={Time}ms, Date={Date}, Country={Country}, Mii='{Mii}'", 
                ghostData.TrackId, ghostData.CharacterId, ghostData.VehicleId, ghostData.ControllerType, 
                ghostData.TotalTimeMs, ghostData.Date, ghostData.Country, ghostData.MiiData);
            
            Log.Debug("Raw bytes: 0x04={B04:X2}, 0x05={B05:X2}, 0x06={B06:X2}, 0x07={B07:X2}, 0x08={B08:X2}, 0x09={B09:X2}, 0x0A={B0A:X2}, 0x0B={B0B:X2}, 0x10={B10:X2}", 
                data[0x04], data[0x05], data[0x06], data[0x07], data[0x08], data[0x09], data[0x0A], data[0x0B], data[0x10]);
            
            var timeMinutes = ExtractBits(data, 0x04, 0, 7);
            var timeSeconds = ExtractBits(data, 0x04, 7, 7);
            var timeMs = ExtractBits(data, 0x05, 6, 10);
            Log.Debug("Time bit extraction: {Minutes}m {Seconds}s {Milliseconds}ms", timeMinutes, timeSeconds, timeMs);
            
            var vehicleId = ExtractBits(data, 0x08, 0, 6);
            var characterId = ExtractBits(data, 0x08, 6, 6);
            Log.Debug("Character/Vehicle extraction: VehicleId={VehicleId}, CharacterId={CharacterId}", vehicleId, characterId);
            
            Log.Debug("Successfully parsed RKG: {FilePath} -> TrackId: 0x{TrackId:X8}, Time: {Time}ms, Laps: {LapCount}", 
                filePath, ghostData.TrackId, ghostData.TotalTimeMs, ghostData.LapSplitsMs.Count);
            
            return ghostData;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error parsing RKG file {FilePath}: {Message}", filePath, ex.Message);
            return null;
        }
    }

    private static bool IsValidRkgFile(byte[] data)
    {
        // RKG files should start with "RKGD" magic bytes
        return data.Length >= 4 && 
               data[0] == 0x52 && data[1] == 0x4B && data[2] == 0x47 && data[3] == 0x44; // "RKGD"
    }

    /// <summary>
    /// Bit reader class for continuous bitstream reading
    /// </summary>
    private class BitReader
    {
        private readonly byte[] data;
        private int bitOffset;

        public BitReader(byte[] data, int startByteOffset = 0)
        {
            this.data = data;
            this.bitOffset = startByteOffset * 8;
        }

        public uint ReadBits(int bitCount)
        {
            uint result = 0;
            
            for (int i = 0; i < bitCount; i++)
            {
                int byteIndex = bitOffset / 8;
                int bitIndex = bitOffset % 8;
                
                if (byteIndex >= data.Length) break;
                
                uint bit = (uint)((data[byteIndex] >> (7 - bitIndex)) & 1);
                result = (result << 1) | bit;
                
                bitOffset++;
            }
            
            return result;
        }
    }

    /// <summary>
    /// Extract bits from byte array with precise bit-level control
    /// </summary>
    private static uint ExtractBits(byte[] data, int byteOffset, int bitOffset, int bitCount)
    {
        uint result = 0;
        int currentByte = byteOffset;
        int currentBit = bitOffset;
        
        for (int i = 0; i < bitCount; i++)
        {
            if (currentByte >= data.Length) break;
            
            uint bit = (uint)((data[currentByte] >> (7 - currentBit)) & 1);
            result = (result << 1) | bit;
            
            currentBit++;
            if (currentBit >= 8)
            {
                currentBit = 0;
                currentByte++;
            }
        }
        
        return result;
    }

    /// <summary>
    /// Parse track ID from offset 0x07 (6 bits, bits 0-5)
    /// </summary>
    private static uint ParseTrackId(byte[] data)
    {
        return ExtractBits(data, 0x07, 0, 6); // 6 bits starting at bit 0 of byte 0x07
    }

    /// <summary>
    /// Parse vehicle ID from offset 0x08 (6 bits, bits 0-5)
    /// </summary>
    private static byte ParseVehicleId(byte[] data)
    {
        return (byte)ExtractBits(data, 0x08, 0, 6); // 6 bits starting at bit 0 of byte 0x08
    }

    /// <summary>
    /// Parse character ID from offset 0x08 (next 6 bits after vehicle ID)
    /// </summary>
    private static byte ParseCharacterId(byte[] data)
    {
        return (byte)ExtractBits(data, 0x08, 6, 6); // 6 bits starting at bit 6 of byte 0x08
    }

    /// <summary>
    /// Parse controller type from offset 0x0B.4 (4 bits, upper 4 bits)
    /// </summary>
    private static byte ParseControllerType(byte[] data)
    {
        return (byte)ExtractBits(data, 0x0B, 0, 4); // 4 bits starting at bit 0 of byte 0x0B
    }

    /// <summary>
    /// Parse finish time using precise bit extraction
    /// 7 bits minutes + 7 bits seconds + 10 bits milliseconds = 24 bits total starting at 0x04
    /// </summary>
    private static uint ParseFinishTime(byte[] data)
    {
        // Extract time fields using bit-level precision
        uint minutes = ExtractBits(data, 0x04, 0, 7);        // 7 bits for minutes
        uint seconds = ExtractBits(data, 0x04, 7, 7);        // Next 7 bits for seconds  
        uint milliseconds = ExtractBits(data, 0x05, 6, 10);  // Next 10 bits for milliseconds

        return (minutes * 60 * 1000) + (seconds * 1000) + milliseconds;
    }

    /// <summary>
    /// Parse date using precise bit extraction
    /// 7 bits year + 4 bits month + 5 bits day starting at 0x09.4
    /// </summary>
    private static DateTime ParseDate(byte[] data, string filePath)
    {
        try
        {
            uint year = 2000 + ExtractBits(data, 0x09, 4, 7);   // 7 bits starting at bit 4 of 0x09
            uint month = ExtractBits(data, 0x0A, 3, 4);          // 4 bits starting at bit 3 of 0x0A  
            uint day = ExtractBits(data, 0x0A, 7, 5);            // 5 bits starting at bit 7 of 0x0A (spans to 0x0B)

            // Validate date ranges
            if (year >= 2000 && year <= DateTime.Now.Year + 1 && 
                month >= 1 && month <= 12 && 
                day >= 1 && day <= 31)
            {
                return new DateTime((int)year, (int)month, (int)day);
            }
            
            Log.Debug("Invalid date in RKG: Year={Year}, Month={Month}, Day={Day}", year, month, day);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to parse date from RKG file");
        }

        return File.GetCreationTime(filePath);
    }

    /// <summary>
    /// Parse lap splits from offset 0x11 using continuous bitstream reading
    /// Each split: 7 bits minutes + 7 bits seconds + 10 bits milliseconds (24 bits total)
    /// CRITICAL: Must read as continuous bitstream, NOT byte-aligned!
    /// </summary>
    private static List<uint> ParseLapSplits(byte[] data)
    {
        var lapSplits = new List<uint>();
        
        // Get lap count from offset 0x10
        if (data.Length <= 0x10) return lapSplits;
        int lapCount = Math.Min((int)data[0x10], 5); // Max 5 lap splits stored
        
        // Create bit reader starting at offset 0x11
        var bitReader = new BitReader(data, 0x11);
        
        Log.Debug("Parsing {LapCount} lap splits starting at offset 0x11", lapCount);
        
        for (int i = 0; i < lapCount; i++)
        {
            // Read exactly 24 bits per lap split (7+7+10)
            uint minutes = bitReader.ReadBits(7);
            uint seconds = bitReader.ReadBits(7); 
            uint milliseconds = bitReader.ReadBits(10);

            uint lapTimeMs = (minutes * 60 * 1000) + (seconds * 1000) + milliseconds;
            
            Log.Debug("Lap {Index}: {Minutes}m {Seconds}s {Milliseconds}ms = {TotalMs}ms", 
                i + 1, minutes, seconds, milliseconds, lapTimeMs);
            
            if (lapTimeMs > 0)
            {
                lapSplits.Add(lapTimeMs);
            }
        }

        return lapSplits;
    }

    /// <summary>
    /// Parse country code from offset 0x34
    /// </summary>
    private static ushort ParseCountryCode(byte[] data)
    {
        if (0x34 < data.Length)
            return data[0x34];
        return 0xFF;
    }

    /// <summary>
    /// Parse Mii data from offset 0x3C (74 bytes)
    /// The Mii name is stored as UTF-16 Big Endian at offset 0x02 within the Mii structure (20 bytes, 10 characters max)
    /// </summary>
    private static string ParseMiiData(byte[] data)
    {
        try
        {
            const int miiDataOffset = 0x3C;
            const int nameOffsetInMii = 0x02;
            const int nameLength = 20; // 10 UTF-16 characters = 20 bytes
            
            if (data.Length < miiDataOffset + nameOffsetInMii + nameLength)
            {
                Log.Debug("RKG file too small for Mii name data");
                return "Unknown";
            }

            var nameBytes = new byte[nameLength];
            Array.Copy(data, miiDataOffset + nameOffsetInMii, nameBytes, 0, nameLength);
            
            Log.Debug("Mii name bytes: {Bytes}", Convert.ToHexString(nameBytes));
            
            var miiName = Encoding.BigEndianUnicode.GetString(nameBytes)
                .TrimEnd('\0')
                .TrimEnd();
            
            if (!string.IsNullOrEmpty(miiName))
            {
                miiName = new string(miiName.Where(c => !char.IsControl(c) && c != '\0' && (c >= 32 && c <= 126 || c > 127)).ToArray());
            }
            
            if (string.IsNullOrWhiteSpace(miiName) || miiName.All(c => c < 32 || (c > 127 && c < 160)))
            {
                Log.Debug("UTF-16 BE failed, trying UTF-16 LE for Mii name");
                miiName = Encoding.Unicode.GetString(nameBytes)
                    .TrimEnd('\0')
                    .TrimEnd();
                    
                if (!string.IsNullOrEmpty(miiName))
                {
                    miiName = new string(miiName.Where(c => !char.IsControl(c) && c != '\0' && (c >= 32 && c <= 126 || c > 127)).ToArray());
                }
            }
            
            Log.Debug("Parsed Mii name: '{Name}' (length: {Length})", miiName, miiName?.Length ?? 0);
            
            return string.IsNullOrWhiteSpace(miiName) ? "Unknown" : miiName.Trim();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error parsing Mii data");
            return "Unknown";
        }
    }
}