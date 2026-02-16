namespace WheelWizard.Helpers;

public static class CrcHelper
{
    public static ushort ComputeCrc16Ccitt(byte[] buffer, int offset, int length)
    {
        const ushort poly = 0x1021;
        ushort crc = 0x0000;

        for (var i = offset; i < offset + length; i++)
        {
            crc ^= (ushort)(buffer[i] << 8);
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ poly) : (ushort)(crc << 1);
        }

        return crc;
    }

    public static uint ComputeCrc32(byte[] data, int offset, int length)
    {
        const uint poly = 0xEDB88320;
        uint crc = 0xFFFFFFFF;

        for (var i = offset; i < offset + length; i++)
        {
            crc ^= data[i];
            for (var j = 0; j < 8; j++)
            {
                if ((crc & 1) != 0)
                    crc = (crc >> 1) ^ poly;
                else
                    crc >>= 1;
            }
        }

        return ~crc;
    }
}
