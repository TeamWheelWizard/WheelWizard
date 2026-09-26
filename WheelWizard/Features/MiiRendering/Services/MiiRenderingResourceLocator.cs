using System.IO.Abstractions;
using WheelWizard.MiiRendering.Configuration;

namespace WheelWizard.MiiRendering.Services;

public sealed class MiiRenderingResourceLocator(IFileSystem fileSystem, MiiRenderingConfiguration configuration, IMiiRenderingPaths paths)
    : IMiiRenderingResourceLocator
{
    public OperationResult<string> GetFflResourcePath()
    {
        var normalized = NormalizeCandidate(paths.ManagedResourcePath);
        if (!string.IsNullOrWhiteSpace(normalized) && fileSystem.File.Exists(normalized))
        {
            var length = fileSystem.FileInfo.New(normalized).Length;
            if (length < configuration.MinimumExpectedSizeBytes)
            {
                return Fail(
                    $"Found {MiiRenderingConfiguration.ResourceFileName} at '{normalized}', but it is only {length} bytes. "
                        + "This file appears invalid or incomplete."
                );
            }

            return normalized;
        }

        return Fail(
            "Unable to locate FFLResHigh.dat. Install it through Wheel Wizard's startup setup window, "
                + "or place it in the managed Mii rendering folder. "
                + $"Probed: {normalized}"
        );
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
