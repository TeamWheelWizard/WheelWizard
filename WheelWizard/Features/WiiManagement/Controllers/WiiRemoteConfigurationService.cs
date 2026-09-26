using System.IO.Abstractions;

namespace WheelWizard.WiiManagement.Controllers;

public interface IWiiRemoteConfigurationService
{
    void SetVirtualRemoteEnabled(string configDirectory, bool enabled);
}

public sealed class WiiRemoteConfigurationService(IFileSystem fileSystem) : IWiiRemoteConfigurationService
{
    private const string WiimoteSection = "[Wiimote1]";
    private const string SourceParameter = "Source";

    public void SetVirtualRemoteEnabled(string configDirectory, bool enabled)
    {
        var configPath = fileSystem.Path.Combine(configDirectory, "WiimoteNew.ini");
        var sourceValue = enabled ? 1 : 0;

        // I rather not translate this message, makes it easier to check where a given error came from
        if (string.IsNullOrEmpty(configPath) || !fileSystem.File.Exists(configPath))
            throw new FileNotFoundException("WiiMote configuration file not found.");

        var lines = fileSystem.File.ReadAllLines(configPath);
        var inWiimote1Section = false;
        var sourceModified = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == WiimoteSection)
            {
                inWiimote1Section = true;
                continue;
            }

            if (!inWiimote1Section)
                continue;
            if (lines[i].Trim().StartsWith("[")) // New section started
                break;

            if (!lines[i].Trim().StartsWith($"{SourceParameter} ="))
                continue;
            lines[i] = $"{SourceParameter} = {sourceValue}";
            sourceModified = true;
            break;
        }

        if (!sourceModified)
        {
            // Source parameter not found, add it to the Wiimote1 section
            var sectionIndex = Array.FindIndex(lines, l => l.Trim() == WiimoteSection);
            if (sectionIndex == -1)
            {
                fileSystem.File.WriteAllLines(configPath, [.. lines, WiimoteSection, $"{SourceParameter} = {sourceValue}"]);
                return;
            }
            var insertIndex = sectionIndex + 1;
            Array.Resize(ref lines, lines.Length + 1);
            Array.Copy(lines, insertIndex, lines, insertIndex + 1, lines.Length - insertIndex - 1);
            lines[insertIndex] = $"{SourceParameter} = {sourceValue}";
        }

        fileSystem.File.WriteAllLines(configPath, lines);
    }
}
