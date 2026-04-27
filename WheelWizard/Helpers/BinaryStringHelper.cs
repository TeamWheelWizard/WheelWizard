using System.Text;

namespace WheelWizard.Helpers;

public static class BinaryStringHelper
{
    private static readonly Encoding Ascii = Encoding.ASCII;

    public static string ReadAscii(byte[] bytes, int offset, int length)
    {
        if (offset < 0 || offset + length > bytes.Length)
            return string.Empty;

        return Ascii.GetString(bytes, offset, length);
    }

    public static string ReadNullTerminatedAscii(byte[] bytes, int offset)
    {
        var end = offset;
        while (end < bytes.Length && bytes[end] != 0)
            end++;

        return Ascii.GetString(bytes, offset, end - offset);
    }
}
