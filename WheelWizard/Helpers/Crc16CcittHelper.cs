namespace WheelWizard.Helpers;

public static class Crc16CcittHelper
{
    public static ushort Compute(byte[] buffer, int offset, int length)
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
}
