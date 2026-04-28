using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using WheelWizard.Helpers;
using WheelWizard.Models.Mods;
using WheelWizard.Services;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Features.Patches;

public static class ModPatchCompatibilityText
{
    public const string IncompatibleTitle = "INCOMPATIBLE MOD";
    public const string IncompatibleMessage =
        "This mod is incompatible with other mods. Launching with this mod enabled may cause incorrect behavior or crashes. Consider converting it to patches by right-clicking the mod.";
}

public interface IModPatchConversionService
{
    bool HasIncompatibleSzsFiles(Mod mod);

    IReadOnlyList<string> GetConvertibleArchiveFiles(Mod mod);

    void RefreshCompatibility(Mod mod);

    Task<OperationResult<ModPatchConversionResult>> ConvertToPatchesAsync(Mod mod, CancellationToken cancellationToken);
}

public sealed class ModPatchConversionService(ISzsPatchConverter szsPatchConverter, ILogger<ModPatchConversionService> logger)
    : IModPatchConversionService
{
    public bool HasIncompatibleSzsFiles(Mod mod) => GetConvertibleArchiveFiles(mod).Any();

    public IReadOnlyList<string> GetConvertibleArchiveFiles(Mod mod)
    {
        var modDirectory = PathManager.GetModDirectoryPath(mod.Title);
        if (!Directory.Exists(modDirectory))
            return [];

        return Directory.EnumerateFiles(modDirectory, "*", SearchOption.AllDirectories).Where(IsConvertibleArchiveFile).ToArray();
    }

    public void RefreshCompatibility(Mod mod)
    {
        mod.HasIncompatibleFiles = HasIncompatibleSzsFiles(mod);
    }

    public async Task<OperationResult<ModPatchConversionResult>> ConvertToPatchesAsync(Mod mod, CancellationToken cancellationToken)
    {
        var sourceDirectory = PathManager.GetModDirectoryPath(mod.Title);
        if (!Directory.Exists(sourceDirectory))
            return Fail("The mod folder does not exist.");

        var sourceFiles = GetConvertibleArchiveFiles(mod);
        if (sourceFiles.Count == 0)
        {
            RefreshCompatibility(mod);
            return new ModPatchConversionResult();
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "WheelWizardPatchConversion", $"{mod.Title}-{Guid.NewGuid():N}");
        var tempModDirectory = Path.Combine(tempRoot, mod.Title);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var progressWindow = new ProgressWindow("Converting mod to patches")
            .SetGoal($"Converting {sourceFiles.Count} file{(sourceFiles.Count == 1 ? string.Empty : "s")}")
            .SetCancellationTokenSource(cts);
        progressWindow.Show();

        try
        {
            var result = await Task.Run(
                () =>
                {
                    CopyDirectory(sourceDirectory, tempModDirectory, cts.Token);
                    var tempFiles = GetConvertibleArchiveFilesInDirectory(tempModDirectory);
                    var warnings = new List<string>();
                    var skipped = new List<string>();
                    var convertedCount = 0;
                    var writtenPatchCount = 0;

                    for (var index = 0; index < tempFiles.Count; index++)
                    {
                        cts.Token.ThrowIfCancellationRequested();
                        var file = tempFiles[index];
                        var fileName = Path.GetFileName(file);

                        Dispatcher.UIThread.Post(() =>
                        {
                            progressWindow.UpdateProgress((int)(index / (double)Math.Max(tempFiles.Count, 1) * 80));
                            progressWindow.SetExtraText($"Converting {fileName}");
                        });

                        if (LooseBrsarPatchFileName.TryGetNormalizedFileName(fileName, out var normalizedPatchFileName))
                        {
                            var renameResult = RenameLooseBrsarPatchFile(file, normalizedPatchFileName);
                            if (renameResult.IsFailure)
                            {
                                skipped.Add($"{fileName}: {renameResult.Error.Message}");
                                continue;
                            }

                            convertedCount++;
                            writtenPatchCount++;
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
                            skipped.Add($"{fileName} is not present in the built-in game baseline.");
                            continue;
                        }

                        warnings.AddRange(conversion.Analysis.Warnings.Select(warning => $"{fileName}: {warning}"));
                        skipped.AddRange(conversion.Analysis.Skipped.Select(item => $"{fileName}: {item}"));

                        if (conversion.Analysis.Skipped.Count > 0)
                            continue;

                        foreach (var entry in conversion.Analysis.Entries)
                        {
                            var destination = Path.Combine(Path.GetDirectoryName(file)!, entry.ExportPath);
                            var destinationDirectory = Path.GetDirectoryName(destination)!;
                            if (!Directory.Exists(destinationDirectory))
                                Directory.CreateDirectory(destinationDirectory);
                            File.WriteAllBytes(destination, entry.Bytes);
                            writtenPatchCount++;
                        }

                        File.Delete(file);
                        convertedCount++;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        progressWindow.UpdateProgress(85);
                        progressWindow.SetExtraText("Applying converted mod");
                    });

                    ReplaceDirectory(sourceDirectory, tempModDirectory, cts.Token);
                    return new ModPatchConversionResult
                    {
                        ConvertedFileCount = convertedCount,
                        WrittenPatchCount = writtenPatchCount,
                        Warnings = warnings,
                        Skipped = skipped,
                    };
                },
                cts.Token
            );

            RefreshCompatibility(mod);
            Dispatcher.UIThread.Post(() => progressWindow.UpdateProgress(100));
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
            progressWindow.Close();
            _ = FileHelper.DeleteDirectoryIfExists(tempRoot);
        }
    }

    private OperationResult<ArchiveConversion> ConvertArchiveFile(string file)
    {
        try
        {
            var fileBytes = File.ReadAllBytes(file);
            var baseline = SelectBaseline(Path.GetFileName(file), fileBytes);
            if (baseline == null)
                return new ArchiveConversion(null, new PatchConversionAnalysis());

            return new ArchiveConversion(baseline, AnalyzeArchive(baseline, Path.GetFileName(file), fileBytes));
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
        var candidates = GameBaselineStore.Instance.FindCandidates(fileName, kind);
        if (candidates.Count == 0)
            return null;

        return candidates
            .Select(candidate => new { Candidate = candidate, Entry = GameBaselineStore.Instance.GetEntry(candidate.Id) })
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

    private PatchConversionAnalysis AnalyzeArchive(BaselineEntry baseline, string fileName, byte[] fileBytes) =>
        string.Equals(baseline.Kind, "brsar", StringComparison.OrdinalIgnoreCase)
            ? BrsarPatchConverter.AnalyzeAgainstBaseline(baseline, fileName, fileBytes)
            : szsPatchConverter.AnalyzeAgainstBaseline(baseline, fileName, fileBytes);

    private int EstimateDifference(BaselineEntry baseline, byte[] fileBytes) =>
        string.Equals(baseline.Kind, "brsar", StringComparison.OrdinalIgnoreCase)
            ? BrsarPatchConverter.EstimateDifference(baseline, fileBytes)
            : szsPatchConverter.EstimateDifference(baseline, fileBytes);

    private static IReadOnlyList<string> GetConvertibleArchiveFilesInDirectory(string directory) =>
        Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(IsConvertibleArchiveFile).ToArray();

    private static bool IsConvertibleArchiveFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        if (LooseBrsarPatchFileName.TryGetNormalizedFileName(fileName, out _))
            return true;

        if (IsBrsarFileName(fileName))
            return true;

        return Path.GetExtension(filePath).Equals(".szs", StringComparison.OrdinalIgnoreCase)
            && !KartSzsAllowList.IsAllowedFullCharacterOrKart(fileName);
    }

    private static bool IsBrsarFileName(string fileName) => fileName.Equals("revo_kart.brsar", StringComparison.OrdinalIgnoreCase);

    private static OperationResult RenameLooseBrsarPatchFile(string file, string normalizedPatchFileName)
    {
        var destination = Path.Combine(Path.GetDirectoryName(file)!, normalizedPatchFileName);
        if (File.Exists(destination) && !FileHelper.PathsEqual(file, destination))
            return Fail($"{normalizedPatchFileName} already exists.");

        File.Move(file, destination, overwrite: true);
        return Ok();
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = Path.Combine(destinationDirectory, Path.GetRelativePath(sourceDirectory, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, true);
        }
    }

    private static void ReplaceDirectory(string sourceDirectory, string convertedDirectory, CancellationToken cancellationToken)
    {
        var backupDirectory = $"{sourceDirectory}.patch-conversion-backup-{Guid.NewGuid():N}";

        Directory.Move(sourceDirectory, backupDirectory);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyDirectory(convertedDirectory, sourceDirectory, cancellationToken);
            _ = FileHelper.DeleteDirectoryIfExists(backupDirectory);
        }
        catch
        {
            if (Directory.Exists(sourceDirectory))
                Directory.Delete(sourceDirectory, true);
            Directory.Move(backupDirectory, sourceDirectory);
            throw;
        }
    }

    private sealed record ArchiveConversion(BaselineEntry? Baseline, PatchConversionAnalysis Analysis);
}
