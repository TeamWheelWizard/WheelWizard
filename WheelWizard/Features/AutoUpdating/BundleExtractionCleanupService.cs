using System.Diagnostics;
using System.IO.Abstractions;
using Microsoft.Extensions.Logging;

namespace WheelWizard.AutoUpdating;

public interface IBundleExtractionCleanupService
{
    /// <summary>
    /// Removes the single-file extraction folders that were left behind by previous WheelWizard versions.
    /// This runs in the background and never throws.
    /// </summary>
    Task CleanupStaleExtractionsAsync();
}

/// <summary>
/// WheelWizard is published as a self-extracting single-file executable. The .NET host extracts the bundled native
/// libraries to <c>&lt;base&gt;/.net/&lt;executable name&gt;/&lt;bundle id&gt;</c> (e.g. <c>%TEMP%\.net\WheelWizard\...</c>
/// on Windows). Every release has a new bundle id, so every update leaves the previous extraction folder behind.
/// The updater also downloads the new executable as <c>WheelWizard_new</c>, which gets its own extraction folder.
/// This service deletes those stale folders while leaving the one used by the running process untouched.
/// </summary>
public class BundleExtractionCleanupService(IFileSystem fileSystem, ILogger<BundleExtractionCleanupService> logger)
    : IBundleExtractionCleanupService
{
    /// <summary>
    /// Suffix the updaters append to the downloaded executable (e.g. <c>WheelWizard_new.exe</c>).
    /// </summary>
    internal const string UpdateExecutableSuffix = "_new";

    /// <summary>
    /// Name of the folder the .NET host creates its extraction folders in.
    /// </summary>
    internal const string ExtractionRootName = ".net";

    /// <summary>
    /// The runtime property that contains the directories used to resolve native libraries. For a single-file
    /// bundle with extracted native libraries, this includes the extraction directory of the running process.
    /// </summary>
    private const string NativeSearchDirectoriesProperty = "NATIVE_DLL_SEARCH_DIRECTORIES";

    public Task CleanupStaleExtractionsAsync()
    {
        return Task.Run(() =>
        {
            try
            {
                var processPath = Environment.ProcessPath;
                var nativeSearchDirectories = AppContext.GetData(NativeSearchDirectoriesProperty) as string;
                var currentExtractionDirectory = ResolveExtractionDirectory(processPath, nativeSearchDirectories);
                if (currentExtractionDirectory is null)
                {
                    logger.LogDebug("Not running from a self-extracted single-file bundle, skipping extraction cleanup");
                    return;
                }

                logger.LogInformation("Running from single-file extraction folder: {Directory}", currentExtractionDirectory);

                // Another (possibly older) WheelWizard instance might still be loading native libraries from its own
                // extraction folder. Deleting that folder from under it could break it, so leave the cleanup to a later start.
                if (IsAnotherInstanceRunning(processPath!))
                {
                    logger.LogDebug("Another WheelWizard instance is running, skipping extraction cleanup");
                    return;
                }

                var removedCount = CleanupStaleExtractions(currentExtractionDirectory);
                if (removedCount > 0)
                    logger.LogInformation("Removed {Count} stale single-file extraction folder(s)", removedCount);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to clean up stale single-file extraction folders");
            }
        });
    }

    /// <summary>
    /// Finds the extraction directory of the running process based on the native library search directories.
    /// Returns <c>null</c> when the process is not running from a self-extracted single-file bundle.
    /// </summary>
    public string? ResolveExtractionDirectory(string? processPath, string? nativeSearchDirectories)
    {
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(nativeSearchDirectories))
            return null;

        var appName = fileSystem.Path.GetFileNameWithoutExtension(processPath);
        var executableDirectory = fileSystem.Path.GetDirectoryName(processPath);
        if (string.IsNullOrWhiteSpace(appName) || string.IsNullOrWhiteSpace(executableDirectory))
            return null;

        foreach (var searchDirectory in nativeSearchDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = NormalizePath(searchDirectory);
            if (candidate is null || PathEquals(candidate, executableDirectory))
                continue;

            // Expected layout: <base>/.net/<app name>/<bundle id>
            var appDirectory = fileSystem.Path.GetDirectoryName(candidate);
            var extractionRoot = appDirectory is null ? null : fileSystem.Path.GetDirectoryName(appDirectory);
            if (appDirectory is null || extractionRoot is null)
                continue;

            if (!NameEquals(fileSystem.Path.GetFileName(appDirectory), appName))
                continue;
            if (!NameEquals(fileSystem.Path.GetFileName(extractionRoot), ExtractionRootName))
                continue;

            return candidate;
        }

        return null;
    }

    /// <summary>
    /// Deletes every extraction folder that belongs to this application except <paramref name="currentExtractionDirectory"/>,
    /// as well as the extraction folders of the <c>_new</c> executable that the updater downloads.
    /// </summary>
    /// <returns>The number of folders that were removed.</returns>
    public int CleanupStaleExtractions(string currentExtractionDirectory)
    {
        var current = NormalizePath(currentExtractionDirectory);
        var appDirectory = current is null ? null : fileSystem.Path.GetDirectoryName(current);
        var extractionRoot = appDirectory is null ? null : fileSystem.Path.GetDirectoryName(appDirectory);
        if (current is null || appDirectory is null || extractionRoot is null)
            return 0;

        var removedCount = 0;

        // Extraction folders of previous versions of this executable.
        if (fileSystem.Directory.Exists(appDirectory))
        {
            foreach (var directory in fileSystem.Directory.EnumerateDirectories(appDirectory))
            {
                if (PathEquals(directory, current))
                    continue;

                if (TryDeleteDirectory(directory))
                    removedCount++;
            }
        }

        // Extraction folders of the downloaded update executable (e.g. WheelWizard_new).
        var appName = fileSystem.Path.GetFileName(appDirectory);
        var updateAppDirectory = fileSystem.Path.Combine(extractionRoot, appName + UpdateExecutableSuffix);
        if (!PathEquals(updateAppDirectory, appDirectory) && fileSystem.Directory.Exists(updateAppDirectory))
        {
            if (TryDeleteDirectory(updateAppDirectory))
                removedCount++;
        }

        return removedCount;
    }

    /// <summary>
    /// Checks whether another process with the name of this executable (or its <c>_new</c> update counterpart) is running.
    /// When the processes cannot be enumerated, this errs on the side of caution and reports <c>true</c>.
    /// </summary>
    private bool IsAnotherInstanceRunning(string processPath)
    {
        var appName = fileSystem.Path.GetFileNameWithoutExtension(processPath);
        var currentProcessId = Environment.ProcessId;

        foreach (var processName in new[] { appName, appName + UpdateExecutableSuffix })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Could not enumerate running processes named {ProcessName}", processName);
                return true;
            }

            try
            {
                if (processes.Any(process => process.Id != currentProcessId))
                    return true;
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }

        return false;
    }

    private bool TryDeleteDirectory(string directory)
    {
        try
        {
            fileSystem.Directory.Delete(directory, recursive: true);
            logger.LogDebug("Removed stale single-file extraction folder: {Directory}", directory);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Most likely still in use by another (older) WheelWizard process. The .NET host repairs
            // partially deleted extraction folders on the next start, so this is safe to ignore.
            logger.LogDebug(e, "Could not remove single-file extraction folder: {Directory}", directory);
            return false;
        }
    }

    private string? NormalizePath(string path)
    {
        try
        {
            var fullPath = fileSystem.Path.GetFullPath(path);
            var trimmed = fullPath.TrimEnd(fileSystem.Path.DirectorySeparatorChar, fileSystem.Path.AltDirectorySeparatorChar);
            return trimmed.Length == 0 ? fullPath : trimmed;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private bool PathEquals(string left, string right)
    {
        var normalizedLeft = NormalizePath(left);
        var normalizedRight = NormalizePath(right);
        return normalizedLeft is not null && normalizedRight is not null && NameEquals(normalizedLeft, normalizedRight);
    }

    private static bool NameEquals(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(left, right, comparison);
    }
}
