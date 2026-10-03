using System.IO.Abstractions;
using System.Text;
using WheelWizard.Shared.IO;

namespace WheelWizard.Settings;

internal static class SettingsFile
{
    public static void Write(IFileSystem files, string path, string text)
    {
        var result = files.WriteAllBytesAtomic(path, Encoding.UTF8.GetBytes(text));
        if (result.IsFailure)
            throw new IOException(result.Error.Message, result.Error.Exception);
    }

    public static void WriteLines(IFileSystem files, string path, IEnumerable<string> lines) =>
        Write(files, path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
}
