using System.Text;

namespace WheelWizard.Helpers;

public static class BinaryStringHelper
{
    private static readonly Encoding Ascii = Encoding.ASCII;

    public static string ReadAscii(byte[] bytes, int offset, int length)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (offset < 0 || length < 0 || offset > bytes.Length - length)
            throw new InvalidDataException("Unexpected end of file.");

        return Ascii.GetString(bytes, offset, length);
    }

    public static string ReadNullTerminatedAscii(byte[] bytes, int offset)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (offset < 0 || offset >= bytes.Length)
            throw new InvalidDataException("Unexpected end of file.");

        var end = offset;

        while (end < bytes.Length && bytes[end] != 0)
            end++;

        if (end == bytes.Length)
            throw new InvalidDataException("Missing null terminator.");

        return Ascii.GetString(bytes, offset, end - offset);
    }
}
