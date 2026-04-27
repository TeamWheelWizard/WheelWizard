using WheelWizard.Features.Archives;
using static WheelWizard.Features.Patches.PatchConversionHelpers;

namespace WheelWizard.Features.Patches;

public sealed class SzsPatchConverter(ISzsArchiveDecoder archiveDecoder) : ISzsPatchConverter
{
    public PatchConversionAnalysis AnalyzeAgainstBaseline(BaselineEntry baseline, string moddedName, byte[] moddedBytes)
    {
        if (!string.Equals(baseline.Kind, "szs", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected baseline is not an SZS file.");

        var warnings = new List<string>();
        var skipped = new List<string>();
        var archiveTag = baseline.ArchiveTag;

        if (!StripExtension(moddedName).Equals(archiveTag, StringComparison.OrdinalIgnoreCase))
            warnings.Add($"The selected file name differs from the original archive tag \"{archiveTag}.szs\".");

        var moddedU8 = archiveDecoder.TryDecodeU8Archive(moddedBytes);
        var wholeFileHash = HashBytes64(moddedBytes);
        var wholeFileMatches = moddedBytes.Length == baseline.WholeFileSize && wholeFileHash == baseline.WholeFileHash;

        var baselineMode = baseline.Mode ?? string.Empty;
        if (baselineMode == "raw" || moddedU8 == null)
        {
            if (wholeFileMatches)
            {
                warnings.Add("The selected SZS matches the built-in game baseline exactly.");
                return new()
                {
                    CleanName = baseline.RelativePath,
                    ModdedName = moddedName,
                    Mode = "whole-file",
                    Warnings = warnings,
                    Skipped = skipped,
                };
            }

            warnings.Add(
                baselineMode == "u8" && moddedU8 == null
                    ? "The original game file is an archive, but the selected file is not a Yaz0/U8 archive. Exporting a whole-file override instead."
                    : "This SZS is stored as a whole-file baseline. Exporting a whole-file override."
            );

            return new()
            {
                CleanName = baseline.RelativePath,
                ModdedName = moddedName,
                Mode = "whole-file",
                Entries =
                [
                    new PatchConversionEntry($"{archiveTag}.szs", baseline.RelativePath, moddedBytes.ToArray(), "Whole-file override"),
                ],
                Warnings = warnings,
                Skipped = skipped,
            };
        }

        var baselineMembers = BuildBaselineMembers(baseline);
        var entries = new List<PatchConversionEntry>();

        foreach (var (logicalPath, moddedEntry) in moddedU8.Files)
        {
            baselineMembers.TryGetValue(logicalPath, out var baselineMember);
            if (baselineMember == null)
                continue;

            var moddedHash = HashBytes64(moddedEntry);

            if (baselineMember.Size == moddedEntry.Length && baselineMember.Hash == moddedHash)
                continue;

            if (IsBlockedLooseRawOverrideExtension(logicalPath))
            {
                skipped.Add($"{logicalPath} uses an unsupported loose override extension (.kcl, .kmp, .slt).");
                continue;
            }

            entries.Add(new(BuildTaggedPatchName(logicalPath, archiveTag), logicalPath, moddedEntry.ToArray(), "Modified archive member"));
        }

        if (entries.Count == 0 && skipped.Count == 0)
            warnings.Add("No SZS differences were found against the built-in game baseline.");

        return new()
        {
            CleanName = baseline.RelativePath,
            ModdedName = moddedName,
            Mode = "tagged-archive",
            Entries = entries.OrderBy(entry => entry.ExportPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings,
            Skipped = skipped,
        };
    }

    public int EstimateDifference(BaselineEntry baseline, byte[] moddedBytes)
    {
        var wholeFileHash = HashBytes64(moddedBytes);
        if (moddedBytes.Length == baseline.WholeFileSize && wholeFileHash == baseline.WholeFileHash)
            return 0;

        var baselineMode = baseline.Mode ?? string.Empty;
        if (baselineMode == "raw")
            return 1;

        var moddedU8 = archiveDecoder.TryDecodeU8Archive(moddedBytes);
        if (moddedU8 == null)
            return 1;

        var baselineMembers = BuildBaselineMembers(baseline);
        var differences = 0;

        foreach (var (logicalPath, moddedEntry) in moddedU8.Files)
        {
            baselineMembers.TryGetValue(logicalPath, out var baselineMember);
            if (baselineMember == null)
                continue;

            var moddedHash = HashBytes64(moddedEntry);
            if (baselineMember.Size != moddedEntry.Length || baselineMember.Hash != moddedHash)
                differences++;
        }

        return differences;
    }

    private static Dictionary<string, BaselineMember> BuildBaselineMembers(BaselineEntry baseline)
    {
        var members = new Dictionary<string, BaselineMember>(StringComparer.Ordinal);
        if (baseline.Members == null)
            return members;

        foreach (var member in baseline.Members)
        {
            if (member.Length < 3)
                continue;

            var logicalPath = ReadJsonString(member[0]);
            if (!TryGetJsonElement(member[1], out var sizeElement) || !sizeElement.TryGetInt32(out var size))
                continue;

            var hash = ReadJsonString(member[2]);
            if (!string.IsNullOrEmpty(logicalPath) && !string.IsNullOrEmpty(hash))
                members[logicalPath] = new(size, hash);
        }

        return members;
    }

    private static string BuildTaggedPatchName(string logicalPath, string archiveTag)
    {
        var segments = logicalPath.Split('/').Where(segment => segment.Length > 0 && segment != ".").ToArray();
        if (segments.Length == 0)
            throw new InvalidOperationException("Cannot build a tagged override for an empty archive path.");

        var fileName = segments[^1];
        var prefixes = string.Concat(segments[..^1].Select(segment => $"[{segment}]"));
        return $"{prefixes}{fileName}.{archiveTag}";
    }

    private static bool IsBlockedLooseRawOverrideExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".kcl", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".kmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slt", StringComparison.OrdinalIgnoreCase);
    }

    private static string StripExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return string.IsNullOrEmpty(extension) ? fileName : fileName[..^extension.Length];
    }
}
