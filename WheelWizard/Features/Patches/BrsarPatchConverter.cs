using System.Text;
using WheelWizard.Helpers;
using WheelWizard.Resources.Languages;
using static WheelWizard.Features.Patches.PatchConversionHelpers;

namespace WheelWizard.Features.Patches;

public static class BrsarPatchConverter
{
    private const int HeaderScanWindow = 0x4000;
    private static readonly Encoding Utf8 = Encoding.UTF8;

    public static PatchConversionAnalysis AnalyzeAgainstBaseline(BaselineEntry baseline, string moddedName, byte[] moddedBytes)
    {
        if (!string.Equals(baseline.Kind, "brsar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The selected baseline is not a BRSAR file.");

        var warnings = new List<string>();
        var skipped = new List<string>();
        var moddedParse = ParseBrsar(moddedBytes);
        var baselineEntries = BuildBaselineEntries(baseline);
        var allFileIds = baselineEntries.Keys.Concat(moddedParse.Entries.Keys).Distinct().Order().ToArray();
        var entries = new List<PatchConversionEntry>();

        foreach (var fileId in allFileIds)
        {
            baselineEntries.TryGetValue(fileId, out var baselineEntry);
            moddedParse.Entries.TryGetValue(fileId, out var moddedEntry);

            if (baselineEntry == null || moddedEntry == null)
                continue;

            var moddedKind = GetBaselineKindCode(moddedEntry.Kind);
            var moddedMagic = moddedEntry.Magic;
            var moddedHash = HashBytes64(moddedEntry.CompareBytes);
            if (
                baselineEntry.Kind == moddedKind
                && baselineEntry.Magic == moddedMagic
                && baselineEntry.Size == moddedEntry.CompareBytes.Length
                && baselineEntry.Hash == moddedHash
            )
            {
                continue;
            }

            if (moddedEntry is { Kind: BrsarEntryKind.Supported, ExportBytes: not null })
            {
                var extension = GetBrsarEntryExtension(moddedEntry.Magic);
                entries.Add(
                    new($"{fileId}{extension}", $"revo_kart.brsar:{fileId}", moddedEntry.ExportBytes.ToArray(), moddedEntry.Detail)
                );
                continue;
            }

            if (moddedEntry.Kind == BrsarEntryKind.Unsupported)
            {
                skipped.Add(
                    Humanizer.ReplaceDynamic(Phrases.Warning_BrsarFileIdUnsupportedMagic, fileId, moddedEntry.Magic ?? string.Empty)!
                );
                continue;
            }

            if (moddedEntry.Kind == BrsarEntryKind.External)
            {
                skipped.Add(Humanizer.ReplaceDynamic(Phrases.Warning_BrsarFileIdExternal, fileId)!);
                continue;
            }

            skipped.Add(Humanizer.ReplaceDynamic(Phrases.Warning_BrsarFileIdUnresolved, fileId)!);
        }

        var unsupportedSummary = SummarizeUnsupportedBrsarCounts(moddedParse.UnsupportedCounts);
        if (unsupportedSummary != null)
            warnings.Add(Humanizer.ReplaceDynamic(Phrases.Warning_BrsarUnsupportedSummary, unsupportedSummary)!);
        if (moddedParse.ExternalCount > 0)
            warnings.Add(Humanizer.ReplaceDynamic(Phrases.Warning_BrsarExternalCount, moddedParse.ExternalCount)!);
        if (moddedParse.UnresolvedCount > 0)
            warnings.Add(Humanizer.ReplaceDynamic(Phrases.Warning_BrsarUnresolvedCount, moddedParse.UnresolvedCount)!);
        if (entries.Count == 0 && skipped.Count == 0)
            warnings.Add(Phrases.Warning_BrsarNoSupportedDifferences);

        return new()
        {
            CleanName = baseline.RelativePath,
            ModdedName = moddedName,
            Mode = "brsar",
            ArchiveTag = "revo_kart",
            Entries = entries.OrderBy(entry => entry.ExportPath, StringComparer.OrdinalIgnoreCase).ToArray(),
            Warnings = warnings,
            Skipped = skipped,
        };
    }

    public static int EstimateDifference(BaselineEntry baseline, byte[] moddedBytes)
    {
        BrsarParseResult moddedParse;

        try
        {
            moddedParse = ParseBrsar(moddedBytes);
        }
        catch
        {
            return 1;
        }

        var baselineEntries = BuildBaselineEntries(baseline);
        var allFileIds = baselineEntries.Keys.Concat(moddedParse.Entries.Keys).Distinct();
        var differences = 0;

        foreach (var fileId in allFileIds)
        {
            baselineEntries.TryGetValue(fileId, out var baselineEntry);
            moddedParse.Entries.TryGetValue(fileId, out var moddedEntry);
            if (baselineEntry == null || moddedEntry == null)
                continue;

            var moddedKind = GetBaselineKindCode(moddedEntry.Kind);
            var moddedMagic = moddedEntry.Magic;
            var moddedHash = HashBytes64(moddedEntry.CompareBytes);
            if (
                baselineEntry.Kind != moddedKind
                || baselineEntry.Magic != moddedMagic
                || baselineEntry.Size != moddedEntry.CompareBytes.Length
                || baselineEntry.Hash != moddedHash
            )
            {
                differences++;
            }
        }

        return differences;
    }

    private static Dictionary<int, BaselineBrsarEntry> BuildBaselineEntries(BaselineEntry baseline)
    {
        var entries = new Dictionary<int, BaselineBrsarEntry>();
        if (baseline.Entries == null)
            return entries;

        foreach (var entry in baseline.Entries)
        {
            if (entry.Length < 5)
                continue;

            if (!TryGetJsonElement(entry[0], out var fileIdElement) || !fileIdElement.TryGetInt32(out var fileId))
                continue;

            var kind = ReadJsonString(entry[1]);
            var magic = ReadJsonString(entry[2]);

            if (!TryGetJsonElement(entry[3], out var sizeElement) || !sizeElement.TryGetInt32(out var size))
                continue;

            var hash = ReadJsonString(entry[4]);

            if (!string.IsNullOrEmpty(kind) && !string.IsNullOrEmpty(hash))
                entries[fileId] = new(kind, magic, size, hash);
        }

        return entries;
    }

    private static BrsarParseResult ParseBrsar(byte[] bytes)
    {
        if (BinaryStringHelper.ReadAscii(bytes, 0, 4) != "RSAR")
            throw new InvalidDataException("The selected BRSAR file does not start with the RSAR header.");

        var infoOffset = BigEndianBinaryHelper.BufferToInt32(bytes, 0x18);
        var infoBase = infoOffset + 0x08;
        var fileTableOffset = ResolveDataRef(bytes, infoBase + 0x18, infoBase);
        var groupTableOffset = ResolveDataRef(bytes, infoBase + 0x20, infoBase);

        if (fileTableOffset == null || groupTableOffset == null)
            throw new InvalidDataException("The BRSAR file is missing the INFO file/group tables.");

        var (knownHeaders, rwarHeaders) = ScanBrsarHeaders(bytes);
        var groupEntryOffsets = ParseReferenceTable(bytes, groupTableOffset.Value, infoBase);
        var groups = groupEntryOffsets.Select((offset, groupIndex) => ParseGroupInfo(bytes, offset, infoBase, groupIndex)).ToArray();
        var fileEntryOffsets = ParseReferenceTable(bytes, fileTableOffset.Value, infoBase);
        var entries = new Dictionary<int, BrsarEntry>();
        var unsupportedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var externalCount = 0;
        var unresolvedCount = 0;

        for (var fileId = 0; fileId < fileEntryOffsets.Count; fileId++)
        {
            var entryOffset = fileEntryOffsets[fileId];
            var declaredFileSize = BigEndianBinaryHelper.BufferToInt32(bytes, entryOffset);
            var declaredWaveSize = BigEndianBinaryHelper.BufferToInt32(bytes, entryOffset + 0x04);
            var externalNameRef = ResolveDataRef(bytes, entryOffset + 0x0c, infoBase);
            if (externalNameRef != null)
            {
                externalCount++;
                var externalPath = BinaryStringHelper.ReadNullTerminatedAscii(bytes, externalNameRef.Value);
                entries[fileId] = new(
                    BrsarEntryKind.External,
                    Utf8.GetBytes(externalPath),
                    null,
                    null,
                    externalPath.Length > 0
                        ? Humanizer.ReplaceDynamic(Phrases.Text_ExternalReferenceWithPath, externalPath)!
                        : Phrases.Text_ExternalReference
                );
                continue;
            }

            var positionTableOffset = ResolveDataRef(bytes, entryOffset + 0x14, infoBase);
            if (positionTableOffset == null)
            {
                unresolvedCount++;
                entries[fileId] = BrsarEntry.Unresolved("Missing file-position table");
                continue;
            }

            var positions = ParseReferenceTable(bytes, positionTableOffset.Value, infoBase);
            if (positions.Count == 0)
            {
                unresolvedCount++;
                entries[fileId] = BrsarEntry.Unresolved("Empty file-position table");
                continue;
            }

            var firstPosition = positions[0];
            var groupIndex = BigEndianBinaryHelper.BufferToInt32(bytes, firstPosition);
            var itemIndex = BigEndianBinaryHelper.BufferToInt32(bytes, firstPosition + 4);
            var group = groupIndex >= 0 && groupIndex < groups.Length ? groups[groupIndex] : null;

            if (group == null || itemIndex < 0 || itemIndex >= group.ItemOffsets.Count)
            {
                unresolvedCount++;
                entries[fileId] = BrsarEntry.Unresolved("Invalid group/item reference");
                continue;
            }

            var itemOffset = group.ItemOffsets[itemIndex];
            var fileRelativeOffset = BigEndianBinaryHelper.BufferToInt32(bytes, itemOffset + 0x04);
            var audioRelativeOffset = BigEndianBinaryHelper.BufferToInt32(bytes, itemOffset + 0x0c);
            var mainGuess = group.FileDataOffset + fileRelativeOffset;
            var waveGuess = group.AudioDataOffset + audioRelativeOffset;
            var mainHeader = FindNearestBrsarHeader(knownHeaders, mainGuess, declaredFileSize);

            if (mainHeader == null)
            {
                unresolvedCount++;
                entries[fileId] = BrsarEntry.Unresolved("No nearby embedded sound header was found");
                continue;
            }

            var mainBytes = SliceBytes(bytes, mainHeader.Offset, mainHeader.Offset + mainHeader.Size);
            if (!IsSupportedBrsarMagic(mainHeader.Magic))
            {
                unsupportedCounts[mainHeader.Magic] = unsupportedCounts.GetValueOrDefault(mainHeader.Magic) + 1;
                entries[fileId] = new(
                    BrsarEntryKind.Unsupported,
                    mainBytes,
                    null,
                    mainHeader.Magic,
                    Humanizer.ReplaceDynamic(Phrases.Text_BrsarEntry, mainHeader.Magic, fileId)!
                );
                continue;
            }

            var exportBytes = mainBytes;
            var detail = Humanizer.ReplaceDynamic(Phrases.Text_BrsarEntry, mainHeader.Magic, fileId)!;
            var waveHeader = FindNearestRwarHeader(rwarHeaders, waveGuess, declaredWaveSize, mainHeader.Offset - mainGuess);
            if (declaredWaveSize > 0 && waveHeader != null)
            {
                var waveBytes = SliceBytes(bytes, waveHeader.Offset, waveHeader.Offset + waveHeader.Size);
                exportBytes = JoinWithAlignment(mainBytes, waveBytes, 0x20);
                detail = Humanizer.ReplaceDynamic(Phrases.Text_BrsarEntryWithRwar, mainHeader.Magic, fileId)!;
            }

            entries[fileId] = new(BrsarEntryKind.Supported, exportBytes, exportBytes, mainHeader.Magic, detail);
        }

        return new(entries, externalCount, unresolvedCount, unsupportedCounts);
    }

    private static GroupInfo ParseGroupInfo(byte[] bytes, int entryOffset, int infoOffset, int groupIndex)
    {
        var itemTableOffset = ResolveDataRef(bytes, entryOffset + 0x20, infoOffset);
        if (itemTableOffset == null)
            throw new InvalidDataException($"Group {groupIndex} does not contain an item table.");

        return new(
            BigEndianBinaryHelper.BufferToInt32(bytes, entryOffset + 0x10),
            BigEndianBinaryHelper.BufferToInt32(bytes, entryOffset + 0x18),
            ParseReferenceTable(bytes, itemTableOffset.Value, infoOffset)
        );
    }

    private static List<int> ParseReferenceTable(byte[] bytes, int tableOffset, int baseAddress)
    {
        var count = BigEndianBinaryHelper.BufferToInt32(bytes, tableOffset);
        var offsets = new List<int>();

        for (var index = 0; index < count; index++)
        {
            var target = ResolveDataRef(bytes, tableOffset + 4 + index * 8, baseAddress);
            if (target != null)
                offsets.Add(target.Value);
        }

        return offsets;
    }

    private static int? ResolveDataRef(byte[] bytes, int refOffset, int baseAddress)
    {
        if (refOffset < 0 || refOffset + 8 > bytes.Length)
            return null;

        var refType = bytes[refOffset];
        var value = BigEndianBinaryHelper.BufferToInt32(bytes, refOffset + 4);
        if (value == 0)
            return null;
        if (refType == 0)
            return value;
        if (refType == 1)
            return baseAddress + value;

        throw new InvalidDataException($"Unsupported BRSAR data reference type {refType}.");
    }

    private static (List<KnownBrsarHeader> KnownHeaders, List<RwarHeader> RwarHeaders) ScanBrsarHeaders(byte[] bytes)
    {
        var knownHeaders = new List<KnownBrsarHeader>();
        var rwarHeaders = new List<RwarHeader>();

        for (var offset = 0; offset + 0x10 <= bytes.Length; offset++)
        {
            if (bytes[offset] != 0x52)
                continue;

            var magic = BinaryStringHelper.ReadAscii(bytes, offset, 4);
            if (magic is not ("RWSD" or "RBNK" or "RWAV" or "RSEQ" or "RSTM" or "RWAR"))
                continue;

            var size = BigEndianBinaryHelper.BufferToInt32(bytes, offset + 0x08);
            if (size < 0x20 || offset + size > bytes.Length)
                continue;

            if (magic == "RWAR")
                rwarHeaders.Add(new(offset, size));
            else
                knownHeaders.Add(new(magic, offset, size));
        }

        return (knownHeaders, rwarHeaders);
    }

    private static KnownBrsarHeader? FindNearestBrsarHeader(List<KnownBrsarHeader> headers, int guessOffset, int expectedSize)
    {
        if (headers.Count == 0)
            return null;

        KnownBrsarHeader? bestHeader = null;
        var bestScore = double.PositiveInfinity;
        var startIndex = LowerBoundHeaderOffset(headers, guessOffset);

        for (var left = startIndex - 1; left >= 0; left--)
        {
            var header = headers[left];
            var distance = guessOffset - header.Offset;
            if (distance > HeaderScanWindow)
                break;

            var score = distance + Math.Min(Math.Abs(header.Size - expectedSize), 0x10000) / 16.0;
            if (score < bestScore)
            {
                bestScore = score;
                bestHeader = header;
            }
        }

        for (var right = startIndex; right < headers.Count; right++)
        {
            var header = headers[right];
            var distance = header.Offset - guessOffset;
            if (distance > HeaderScanWindow)
                break;

            var score = distance + Math.Min(Math.Abs(header.Size - expectedSize), 0x10000) / 16.0;
            if (score < bestScore)
            {
                bestScore = score;
                bestHeader = header;
            }
        }

        return bestHeader;
    }

    private static RwarHeader? FindNearestRwarHeader(List<RwarHeader> headers, int guessOffset, int expectedSize, int preferredDelta)
    {
        if (headers.Count == 0)
            return null;

        RwarHeader? bestHeader = null;
        var bestScore = double.PositiveInfinity;
        var startIndex = LowerBoundHeaderOffset(headers, guessOffset);

        for (var left = startIndex - 1; left >= 0; left--)
        {
            var header = headers[left];
            var distance = guessOffset - header.Offset;
            if (distance > HeaderScanWindow)
                break;

            var relativePenalty = Math.Abs((header.Offset - guessOffset) - preferredDelta) / 2.0;
            var sizePenalty = Math.Min(Math.Abs(header.Size - expectedSize), 0x20000) / 32.0;
            var score = distance + relativePenalty + sizePenalty;
            if (score < bestScore)
            {
                bestScore = score;
                bestHeader = header;
            }
        }

        for (var right = startIndex; right < headers.Count; right++)
        {
            var header = headers[right];
            var distance = header.Offset - guessOffset;
            if (distance > HeaderScanWindow)
                break;

            var relativePenalty = Math.Abs((header.Offset - guessOffset) - preferredDelta) / 2.0;
            var sizePenalty = Math.Min(Math.Abs(header.Size - expectedSize), 0x20000) / 32.0;
            var score = distance + relativePenalty + sizePenalty;
            if (score < bestScore)
            {
                bestScore = score;
                bestHeader = header;
            }
        }

        return bestHeader;
    }

    private static int LowerBoundHeaderOffset<T>(List<T> headers, int guessOffset)
        where T : IHeaderOffset
    {
        var low = 0;
        var high = headers.Count;

        while (low < high)
        {
            var middle = (low + high) >> 1;
            if (headers[middle].Offset < guessOffset)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static string? SummarizeUnsupportedBrsarCounts(Dictionary<string, int> counts)
    {
        var parts = counts
            .Where(entry => entry.Value > 0)
            .OrderByDescending(entry => entry.Value)
            .Select(entry => $"{entry.Value} {entry.Key}")
            .ToArray();

        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    private static string GetBaselineKindCode(BrsarEntryKind kind) =>
        kind switch
        {
            BrsarEntryKind.Supported => "s",
            BrsarEntryKind.Unsupported => "u",
            BrsarEntryKind.External => "e",
            _ => "n",
        };

    private static bool IsSupportedBrsarMagic(string magic) => magic is "RBNK" or "RSEQ" or "RWSD";

    private static string GetBrsarEntryExtension(string? magic) =>
        magic switch
        {
            "RBNK" => ".brbnk",
            "RSEQ" => ".brseq",
            "RWSD" => ".brwsd",
            _ => throw new InvalidOperationException("Cannot export a BRSAR entry without a supported magic."),
        };

    private static byte[] JoinWithAlignment(byte[] first, byte[] second, int alignment)
    {
        var padding = (alignment - (first.Length % alignment)) % alignment;
        var output = new byte[first.Length + padding + second.Length];
        Buffer.BlockCopy(first, 0, output, 0, first.Length);
        Buffer.BlockCopy(second, 0, output, first.Length + padding, second.Length);
        return output;
    }

    private static byte[] SliceBytes(byte[] bytes, int start, int end)
    {
        if (start < 0 || end < start || end > bytes.Length)
            throw new InvalidDataException("Tried to read outside the selected file.");

        return bytes[start..end];
    }

    private enum BrsarEntryKind
    {
        Supported,
        Unsupported,
        External,
        Unresolved,
    }

    private sealed record BrsarParseResult(
        Dictionary<int, BrsarEntry> Entries,
        int ExternalCount,
        int UnresolvedCount,
        Dictionary<string, int> UnsupportedCounts
    );

    private sealed record BrsarEntry(BrsarEntryKind Kind, byte[] CompareBytes, byte[]? ExportBytes, string? Magic, string Detail)
    {
        public static BrsarEntry Unresolved(string detail) => new(BrsarEntryKind.Unresolved, [], null, null, detail);
    }

    private sealed record GroupInfo(int FileDataOffset, int AudioDataOffset, List<int> ItemOffsets);

    private interface IHeaderOffset
    {
        int Offset { get; }
    }

    private sealed record KnownBrsarHeader(string Magic, int Offset, int Size) : IHeaderOffset;

    private sealed record RwarHeader(int Offset, int Size) : IHeaderOffset;
}
