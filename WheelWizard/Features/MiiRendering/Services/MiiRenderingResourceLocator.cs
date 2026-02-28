using System.IO.Abstractions;
using WheelWizard.MiiRendering.Configuration;

namespace WheelWizard.MiiRendering.Services;

public sealed class MiiRenderingResourceLocator(IFileSystem fileSystem, MiiRenderingConfiguration configuration)
    : IMiiRenderingResourceLocator
{
    private string? _resolvedPath;
    private const int MaxAncestorProbeDepth = 10;

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
            "Unable to locate FFLResHigh.dat. Configure WW_FFLRESHIGH_PATH to the file path or directory, "
                + "or place the file in one of the default probe locations (including G:\\Temp). "
                + $"Probed: {probeList}"
        );
    }

    private IEnumerable<string> GetCandidates()
    {
        if (!string.IsNullOrWhiteSpace(configuration.ResourcePath))
            yield return configuration.ResourcePath;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateProbeRoots())
        {
            if (!seen.Add(root))
                continue;

            yield return fileSystem.Path.Combine(root, MiiRenderingConfiguration.ResourceFileName);
            yield return fileSystem.Path.Combine(root, "Resources", MiiRenderingConfiguration.ResourceFileName);
        }

        // Required development fallback path mentioned in project requirements.
        yield return @"G:\Temp\FFLResHigh.dat";
        yield return @"G:\Temp";
        yield return "/mnt/g/Temp/FFLResHigh.dat";
        yield return "/mnt/g/Temp";

        foreach (var directory in configuration.AdditionalSearchDirectories)
            yield return directory;
    }

    private IEnumerable<string> EnumerateProbeRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateRootWithAncestors(Environment.CurrentDirectory))
        {
            if (seen.Add(root))
                yield return root;
        }

        foreach (var root in EnumerateRootWithAncestors(AppContext.BaseDirectory))
        {
            if (seen.Add(root))
                yield return root;
        }
    }

    private IEnumerable<string> EnumerateRootWithAncestors(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
            yield break;

        IDirectoryInfo? current;
        try
        {
            var fullPath = fileSystem.Path.GetFullPath(startDirectory);
            current = fileSystem.DirectoryInfo.New(fullPath);
        }
        catch
        {
            yield break;
        }

        var depth = 0;
        while (current != null && depth++ < MaxAncestorProbeDepth)
        {
            yield return current.FullName;
            current = current.Parent;
        }
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
