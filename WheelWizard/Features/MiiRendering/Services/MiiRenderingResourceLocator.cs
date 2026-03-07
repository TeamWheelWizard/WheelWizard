using System.IO.Abstractions;
using WheelWizard.MiiRendering.Configuration;

namespace WheelWizard.MiiRendering.Services;

public sealed class MiiRenderingResourceLocator(IFileSystem fileSystem, MiiRenderingConfiguration configuration)
    : IMiiRenderingResourceLocator
{
    private string? _resolvedPath;

    public OperationResult<string> GetFflResourcePath()
    {
        if (!string.IsNullOrWhiteSpace(_resolvedPath) && fileSystem.File.Exists(_resolvedPath))
            return _resolvedPath;

        foreach (var candidate in GetCandidates())
        {
            var normalized = NormalizeCandidate(candidate);
            if (string.IsNullOrWhiteSpace(normalized) || !fileSystem.File.Exists(normalized))
                continue;

            var length = fileSystem.FileInfo.New(normalized).Length;
            if (length < configuration.MinimumExpectedSizeBytes)
                return Fail(
                    $"Found {MiiRenderingConfiguration.ResourceFileName} at '{normalized}', but it is only {length} bytes. "
                        + "This file appears invalid or incomplete."
                );

            _resolvedPath = normalized;
            return normalized;
        }

        var probeList = string.Join(
            ", ",
            GetCandidates().Select(NormalizeCandidate).Where(static p => !string.IsNullOrWhiteSpace(p)).Distinct()
        );
        return Fail(
            "Unable to locate FFLResHigh.dat. Install it through Wheel Wizard's startup setup window, "
                + "place it in the managed Mii rendering folder, or configure WW_FFLRESHIGH_PATH to the file path or directory. "
                + $"Probed: {probeList}"
        );
    }

    private IEnumerable<string> GetCandidates()
    {
        if (!string.IsNullOrWhiteSpace(configuration.ResourcePath))
            yield return configuration.ResourcePath;

        if (!string.IsNullOrWhiteSpace(configuration.ManagedResourcePath))
            yield return configuration.ManagedResourcePath;

        foreach (var directory in configuration.AdditionalSearchDirectories)
            yield return directory;
    }

    private string NormalizeCandidate(string candidate)
    {
        try
        {
            var fullPath = fileSystem.Path.GetFullPath(candidate);
            if (fileSystem.Directory.Exists(fullPath))
                return fileSystem.Path.Combine(fullPath, MiiRenderingConfiguration.ResourceFileName);
            return fullPath;
        }
        catch
        {
            return string.Empty;
        }
    }
}
