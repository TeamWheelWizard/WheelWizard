using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using WheelWizard.Features.Archives;
using WheelWizard.Models.Mods;
using WheelWizard.Mods;
using WheelWizard.Shared.IO;

namespace WheelWizard.Features.Patches;

public static class ModPatchCompatibilityText
{
    public static string IncompatibleTitle => t("patch.incompatible_mod.title");
    public static string IncompatibleMessage => t("patch.incompatible_mod.message");
}

public interface IModPatchConversionService
{
    bool HasIncompatibleSzsFiles(Mod mod);

    IReadOnlyList<string> GetConvertibleArchiveFiles(Mod mod);

    void RefreshCompatibility(Mod mod);

    Task<OperationResult<ModPatchConversionResult>> ConvertToPatchesAsync(
        Mod mod,
        CancellationToken cancellationToken,
        IProgress<ModOperationProgress>? progress = null
    );
}

public sealed class ModPatchConversionService(
    ISzsPatchConverter szsPatchConverter,
    ILogger<ModPatchConversionService> logger,
    IFileSystem fileSystem,
    IModPaths paths,
    IGameBaselineStore baselineStore
) : IModPatchConversionService
{
    public bool HasIncompatibleSzsFiles(Mod mod) => GetConvertibleArchiveFiles(mod).Any();

    public IReadOnlyList<string> GetConvertibleArchiveFiles(Mod mod)
    {
        var modDirectory = paths.GetModDirectoryPath(mod.Title);
        if (!fileSystem.Directory.Exists(modDirectory))
            return [];

        return fileSystem
            .Directory.EnumerateFiles(modDirectory, "*", SearchOption.AllDirectories)
            .Where(IsConvertibleArchiveFile)
            .ToArray();
    }

    public void RefreshCompatibility(Mod mod)
    {
        mod.HasIncompatibleFiles = HasIncompatibleSzsFiles(mod);
    }

    public async Task<OperationResult<ModPatchConversionResult>> ConvertToPatchesAsync(
        Mod mod,
        CancellationToken cancellationToken,
        IProgress<ModOperationProgress>? progress = null
    )
    {
        var sourceDirectory = paths.GetModDirectoryPath(mod.Title);
        if (!fileSystem.Directory.Exists(sourceDirectory))
            return Fail(t("message_error.no_mod_folder.extra"));

        var sourceFiles = GetConvertibleArchiveFiles(mod);
        if (sourceFiles.Count == 0)
        {
            RefreshCompatibility(mod);
            return new ModPatchConversionResult();
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "WheelWizardPatchConversion", $"{mod.Title}-{Guid.NewGuid():N}");
        var tempModDirectory = Path.Combine(tempRoot, mod.Title);
        progress?.Report(new(ModOperationStage.Converting, 0, TotalFiles: sourceFiles.Count));

        try
        {
            var result = await Task.Run(
                () =>
                {
                    CopyDirectory(sourceDirectory, tempModDirectory, cancellationToken);
                    var tempFiles = GetConvertibleArchiveFilesInDirectory(tempModDirectory);
                    var warnings = new List<string>();
                    var skipped = new List<string>();
                    var convertedCount = 0;
                    var writtenPatchCount = 0;
                    var archiveBundles = new Dictionary<string, Dictionary<string, byte[]>>(StringComparer.OrdinalIgnoreCase);
                    var archivePrefix = SanitizeArchivePrefix(mod.Title);

                    for (var index = 0; index < tempFiles.Count; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var file = tempFiles[index];
                        var fileName = Path.GetFileName(file);

                        progress?.Report(
                            new(
                                ModOperationStage.Converting,
                                (int)(index / (double)Math.Max(tempFiles.Count, 1) * 80),
                                fileName,
                                tempFiles.Count
                            )
                        );

                        if (LooseBrsarPatchFileName.TryGetNormalizedFileName(fileName, out var normalizedPatchFileName))
                        {
                            AddArchiveBundleEntry(
                                archiveBundles,
                                Path.Combine(Path.GetDirectoryName(file)!, $"{archivePrefix}.revo_kart.szs"),
                                normalizedPatchFileName,
                                fileSystem.File.ReadAllBytes(file)
                            );
                            fileSystem.File.Delete(file);
                            convertedCount++;
                            continue;
                        }

                        var conversionResult = ConvertArchiveFile(file);
                        if (conversionResult.IsFailure)
                        {
                            skipped.Add($"{fileName}: {conversionResult.Error.Message}");
                            continue;
                        }

                        var conversion = conversionResult.Value;
                        if (conversion.Baseline == null)
                        {
                            skipped.Add(t("warning.file_not_in_built_in_baseline", fileName)!);
                            continue;
                        }

                        warnings.AddRange(conversion.Analysis.Warnings.Select(warning => $"{fileName}: {warning}"));
                        skipped.AddRange(conversion.Analysis.Skipped.Select(item => $"{fileName}: {item}"));

                        if (conversion.Analysis.Skipped.Count > 0)
                            continue;

                        if (TryGetBundleTarget(conversion.Analysis, out var bundleTarget))
                        {
                            var bundleDestination = Path.Combine(Path.GetDirectoryName(file)!, $"{archivePrefix}.{bundleTarget}.szs");
                            foreach (var entry in conversion.Analysis.Entries)
                            {
                                if (IsDeletionPatchEntry(entry))
                                {
                                    var deletionDestination = Path.Combine(Path.GetDirectoryName(file)!, entry.ExportPath);
                                    fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(deletionDestination)!);
                                    fileSystem.File.WriteAllBytes(deletionDestination, entry.Bytes);
                                    writtenPatchCount++;
                                    continue;
                                }

                                var memberPath = string.Equals(conversion.Analysis.Mode, "brsar", StringComparison.OrdinalIgnoreCase)
                                    ? entry.ExportPath
                                    : entry.LogicalPath;
                                AddArchiveBundleEntry(archiveBundles, bundleDestination, memberPath, entry.Bytes);
                            }
                        }
                        else
                        {
                            foreach (var entry in conversion.Analysis.Entries)
                            {
                                var destination = Path.Combine(Path.GetDirectoryName(file)!, entry.ExportPath);
                                var destinationDirectory = Path.GetDirectoryName(destination)!;
                                if (!fileSystem.Directory.Exists(destinationDirectory))
                                    fileSystem.Directory.CreateDirectory(destinationDirectory);
                                fileSystem.File.WriteAllBytes(destination, entry.Bytes);
                                writtenPatchCount++;
                            }
                        }

                        fileSystem.File.Delete(file);
                        convertedCount++;
                    }

                    writtenPatchCount += WriteArchiveBundles(archiveBundles, warnings);

                    progress?.Report(new(ModOperationStage.Applying, 85));

                    ReplaceDirectory(sourceDirectory, tempModDirectory, cancellationToken);
                    return new ModPatchConversionResult
                    {
                        ConvertedFileCount = convertedCount,
                        WrittenPatchCount = writtenPatchCount,
                        Warnings = warnings,
                        Skipped = skipped,
                    };
                },
                cancellationToken
            );

            RefreshCompatibility(mod);
            progress?.Report(new(ModOperationStage.Applying, 100));
            return result;
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Patch conversion cancelled for mod {ModTitle}.", mod.Title);
            return Fail("Conversion cancelled.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Patch conversion failed for mod {ModTitle}.", mod.Title);
            return ex;
        }
        finally
        {
            _ = fileSystem.DeleteDirectoryIfExists(tempRoot);
        }
    }

    private OperationResult<ArchiveConversion> ConvertArchiveFile(string file)
    {
        try
        {
            var fileBytes = fileSystem.File.ReadAllBytes(file);
            var baseline = SelectBaseline(Path.GetFileName(file), fileBytes);
            if (baseline == null)
                return new ArchiveConversion(null, new PatchConversionAnalysis());

            var analysisResult = AnalyzeArchive(baseline, Path.GetFileName(file), fileBytes);
            if (analysisResult.IsFailure)
                return analysisResult.Error;

            return new ArchiveConversion(baseline, analysisResult.Value);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to analyze archive {ArchivePath}.", file);
            return new OperationError { Message = ex.Message, Exception = ex };
        }
    }

    private BaselineEntry? SelectBaseline(string fileName, byte[] moddedBytes)
    {
        var kind = IsBrsarFileName(fileName) ? "brsar" : "szs";
        var candidates = baselineStore.FindCandidates(fileName, kind);
        if (candidates.Count == 0)
            return null;

        return candidates
            .Select(candidate => new { Candidate = candidate, Entry = baselineStore.GetEntry(candidate.Id) })
            .Where(item => item.Entry != null)
            .Select(item => new
            {
                item.Candidate,
                Entry = item.Entry!,
                Difference = EstimateDifference(item.Entry!, moddedBytes),
            })
            .OrderBy(item => item.Difference)
            .ThenBy(item => item.Candidate.Region ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault()
            ?.Entry;
    }

    private OperationResult<PatchConversionAnalysis> AnalyzeArchive(BaselineEntry baseline, string fileName, byte[] fileBytes) =>
        string.Equals(baseline.Kind, "brsar", StringComparison.OrdinalIgnoreCase)
            ? BrsarPatchConverter.AnalyzeAgainstBaseline(baseline, fileName, fileBytes)
            : szsPatchConverter.AnalyzeAgainstBaseline(baseline, fileName, fileBytes);

    private int EstimateDifference(BaselineEntry baseline, byte[] fileBytes) =>
        string.Equals(baseline.Kind, "brsar", StringComparison.OrdinalIgnoreCase)
            ? BrsarPatchConverter.EstimateDifference(baseline, fileBytes)
            : szsPatchConverter.EstimateDifference(baseline, fileBytes);

    private IReadOnlyList<string> GetConvertibleArchiveFilesInDirectory(string directory) =>
        fileSystem.Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(IsConvertibleArchiveFile).ToArray();

    private static bool IsConvertibleArchiveFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (LooseBrsarPatchFileName.TryGetNormalizedFileName(fileName, out _))
            return true;

        if (IsBrsarFileName(fileName))
            return true;

        return Path.GetExtension(filePath).Equals(".szs", StringComparison.OrdinalIgnoreCase)
            && !IsModdingArchiveFile(fileName)
            && !KartSzsAllowList.IsAllowedFullCharacterOrKart(fileName);
    }

    private static bool IsBrsarFileName(string fileName) => fileName.Equals("revo_kart.brsar", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetBundleTarget(PatchConversionAnalysis analysis, out string bundleTarget)
    {
        bundleTarget = string.Empty;

        if (string.Equals(analysis.Mode, "brsar", StringComparison.OrdinalIgnoreCase))
        {
            bundleTarget = "revo_kart";
            return true;
        }

        if (
            !string.Equals(analysis.Mode, "tagged-archive", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(analysis.ArchiveTag)
        )
        {
            return false;
        }

        bundleTarget = SanitizeArchivePrefix(analysis.ArchiveTag);
        return true;
    }

    private static bool IsDeletionPatchEntry(PatchConversionEntry entry) =>
        entry.Bytes.Length == 0 && entry.LogicalPath.EndsWith(".delete", StringComparison.OrdinalIgnoreCase);

    private static void AddArchiveBundleEntry(
        Dictionary<string, Dictionary<string, byte[]>> archiveBundles,
        string bundlePath,
        string memberPath,
        byte[] bytes
    )
    {
        if (!archiveBundles.TryGetValue(bundlePath, out var members))
        {
            members = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            archiveBundles[bundlePath] = members;
        }

        if (members.TryGetValue(memberPath, out var existingBytes) && existingBytes.SequenceEqual(bytes))
            return;

        members[memberPath] = bytes;
    }

    private int WriteArchiveBundles(Dictionary<string, Dictionary<string, byte[]>> archiveBundles, List<string> warnings)
    {
        var writtenCount = 0;

        foreach (var (bundlePath, members) in archiveBundles.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (members.Count == 0)
                continue;

            fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(bundlePath)!);
            if (fileSystem.File.Exists(bundlePath))
            {
                warnings.Add($"{Path.GetFileName(bundlePath)} already existed and was replaced with the converted archive bundle.");
                fileSystem.File.Delete(bundlePath);
            }

            fileSystem.File.WriteAllBytes(bundlePath, U8ArchiveBuilder.BuildYaz0(members));
            writtenCount++;
        }

        return writtenCount;
    }

    private static bool IsModdingArchiveFile(string fileName)
    {
        if (!fileName.EndsWith(".szs", StringComparison.OrdinalIgnoreCase))
            return false;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var tagSeparator = nameWithoutExtension.LastIndexOf('.');
        return tagSeparator > 0 && tagSeparator + 1 < nameWithoutExtension.Length;
    }

    private static string SanitizeArchivePrefix(string value)
    {
        var cleaned = new string(
            value.Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_').ToArray()
        ).Trim('_');

        return string.IsNullOrWhiteSpace(cleaned) ? "mod" : cleaned;
    }

    private void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        fileSystem.Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in fileSystem.Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            fileSystem.Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in fileSystem.Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            fileSystem.Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            fileSystem.File.Copy(file, destination, true);
        }
    }

    private void ReplaceDirectory(string sourceDirectory, string convertedDirectory, CancellationToken cancellationToken)
    {
        var backupDirectory = $"{sourceDirectory}.patch-conversion-backup-{Guid.NewGuid():N}";

        fileSystem.Directory.Move(sourceDirectory, backupDirectory);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyDirectory(convertedDirectory, sourceDirectory, cancellationToken);
            _ = fileSystem.DeleteDirectoryIfExists(backupDirectory);
        }
        catch
        {
            if (fileSystem.Directory.Exists(sourceDirectory))
                fileSystem.Directory.Delete(sourceDirectory, true);
            fileSystem.Directory.Move(backupDirectory, sourceDirectory);
            throw;
        }
    }

    private sealed record ArchiveConversion(BaselineEntry? Baseline, PatchConversionAnalysis Analysis);
}
