using System.IO.Abstractions;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpCompress.Archives;
using WheelWizard.ApplicationData;
using WheelWizard.Dolphin.Discovery;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Settings;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;

namespace WheelWizard.DolphinInstaller;

/// <summary>How Wheel Wizard can manage Dolphin on this machine.</summary>
public enum ManagedDolphinKind
{
    Unsupported,

    /// <summary>Windows and macOS: the official build, unpacked into Wheel Wizard's data folder.</summary>
    Portable,

    /// <summary>Native Linux: the official Flatpak, installed for the current user.</summary>
    Flatpak,

    /// <summary>The Wheel Wizard Flatpak ships its own Dolphin extension; only its user folder is ours.</summary>
    Bundled,
}

public sealed record ManagedDolphinProgress(string Message, int Percent);

public interface IManagedDolphinService
{
    ManagedDolphinKind Kind { get; }
    bool IsActive { get; }
    bool IsInstalled { get; }
    string? InstalledVersion { get; }
    string RootFolderPath { get; }
    string UserFolderPath { get; }

    Task<OperationResult> InstallAsync(IProgress<ManagedDolphinProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>Points the Dolphin settings at the managed Dolphin and marks it as managed.</summary>
    OperationResult Activate();

    /// <summary>Stops managing Dolphin; the files stay so switching back is instant.</summary>
    void Deactivate();

    /// <summary>Re-derives the managed paths from the current data folder (it can be relocated).</summary>
    void SyncSettings();

    /// <summary>Copies Miis, saves, controller and graphics settings from another Dolphin into the managed one.</summary>
    OperationResult ImportUserData(string sourceWiiFolderPath, string sourceConfigFolderPath);

    /// <summary>The newer release when the managed Dolphin is out of date, otherwise <see langword="null"/>.</summary>
    Task<DolphinRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> UpdateAsync(IProgress<ManagedDolphinProgress>? progress = null, CancellationToken cancellationToken = default);

    long GetInstalledSize();

    /// <summary>Nothing points into the managed folder, not even a user folder that was handed to the user's own Dolphin.</summary>
    bool CanRemove { get; }

    OperationResult Remove();
}

public sealed class ManagedDolphinService(
    ISettingsManager settings,
    IApplicationDataLocation applicationData,
    IFileSystem fileSystem,
    IRuntimeEnvironment environment,
    IDolphinReleaseClient releases,
    IDownloadService downloads,
    IUnixProcessService processes,
    ILinuxDolphinInstaller linuxInstaller,
    IDolphinDiscoveryService discovery,
    IDolphinPathResolver dolphinPaths,
    IDolphinVersionService versions,
    ILogger<ManagedDolphinService> logger
) : IManagedDolphinService
{
    public const string FlatpakAppId = "org.DolphinEmu.dolphin-emu";
    public const string FlatpakCommand = $"flatpak run {FlatpakAppId}";

    /// <summary>The files worth keeping from another Dolphin besides its NAND; Dolphin.ini holds that install's own paths.</summary>
    private static readonly string[] ImportedConfigFiles = ["GCPadNew.ini", "WiimoteNew.ini", "GFX.ini"];

    public ManagedDolphinKind Kind =>
        environment.IsFlatpakSandboxed(fileSystem) ? ManagedDolphinKind.Bundled
        : environment.IsWindows || environment.IsMacOS ? ManagedDolphinKind.Portable
        : environment.IsLinux ? ManagedDolphinKind.Flatpak
        : ManagedDolphinKind.Unsupported;

    public bool IsActive => settings.Get(settings.DOLPHIN_MANAGED);
    public string RootFolderPath => fileSystem.Path.Combine(applicationData.DirectoryPath, "Dolphin");
    public string UserFolderPath => fileSystem.Path.Combine(RootFolderPath, "User");
    private string AppFolderPath => fileSystem.Path.Combine(RootFolderPath, "App");
    private string DownloadFolderPath => fileSystem.Path.Combine(RootFolderPath, "Download");
    private string VersionFilePath => fileSystem.Path.Combine(RootFolderPath, "version.json");

    public bool IsInstalled =>
        Kind switch
        {
            ManagedDolphinKind.Portable => FindExecutable() is not null,
            ManagedDolphinKind.Flatpak => linuxInstaller.IsDolphinInstalledInFlatpak(),
            ManagedDolphinKind.Bundled => !environment.IsMissingDolphinFlatpakExtension(fileSystem),
            _ => false,
        };

    public string? InstalledVersion
    {
        get
        {
            if (Kind != ManagedDolphinKind.Portable)
                return IsActive ? versions.CheckConfiguredDolphin().Version : null;
            try
            {
                if (!fileSystem.File.Exists(VersionFilePath))
                    return null;
                return JsonSerializer.Deserialize<InstalledRelease>(fileSystem.File.ReadAllText(VersionFilePath))?.ShortRevision;
            }
            catch
            {
                return null;
            }
        }
    }

    public async Task<OperationResult> InstallAsync(
        IProgress<ManagedDolphinProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        switch (Kind)
        {
            case ManagedDolphinKind.Portable:
                var install = await InstallPortableAsync(progress, cancellationToken);
                if (install.IsFailure)
                    return install;
                // The last steps do not stop halfway, so a cancelled install can still complete. Whoever cancelled has
                // moved on, so it stays installed without taking over the Dolphin settings.
                return cancellationToken.IsCancellationRequested ? Fail("The Dolphin download was cancelled.") : Activate();
            case ManagedDolphinKind.Flatpak:
                progress?.Report(new(t("onboarding.dolphin_installing"), 0));
                var flatpak = await linuxInstaller.InstallFlatpakDolphin(
                    new Progress<int>(percent => progress?.Report(new(t("onboarding.dolphin_installing"), percent)))
                );
                return flatpak.IsFailure ? flatpak : Activate();
            case ManagedDolphinKind.Bundled:
                return environment.IsMissingDolphinFlatpakExtension(fileSystem)
                    ? Fail("The Dolphin extension for the Wheel Wizard Flatpak is not installed.")
                    : Activate();
            default:
                return Fail("Wheel Wizard cannot install Dolphin on this system.");
        }
    }

    public OperationResult Activate()
    {
        try
        {
            switch (Kind)
            {
                case ManagedDolphinKind.Portable:
                {
                    var executable = FindExecutable();
                    if (executable is null)
                        return Fail("The managed Dolphin is not installed.");
                    EnsureUserFolder();
                    var location = environment.IsWindows ? executable : ShellQuoting.QuoteUnixArgument(executable);
                    if (!settings.Set(settings.DOLPHIN_LOCATION, location) || !settings.Set(settings.USER_FOLDER_PATH, UserFolderPath))
                        return Fail("Wheel Wizard could not save the managed Dolphin location.");
                    break;
                }
                case ManagedDolphinKind.Flatpak:
                {
                    var userFolder = discovery.FindUserDirectory(FlatpakCommand, string.Empty);
                    if (string.IsNullOrWhiteSpace(userFolder))
                        return Fail("Dolphin was installed, but its user folder could not be found.");
                    if (!settings.Set(settings.DOLPHIN_LOCATION, FlatpakCommand) || !settings.Set(settings.USER_FOLDER_PATH, userFolder))
                        return Fail("Wheel Wizard could not save the managed Dolphin location.");
                    break;
                }
                case ManagedDolphinKind.Bundled:
                    EnsureUserFolder();
                    if (!settings.Set(settings.USER_FOLDER_PATH, UserFolderPath))
                        return Fail("Wheel Wizard could not save the managed Dolphin user folder.");
                    break;
                default:
                    return Fail("Wheel Wizard cannot manage Dolphin on this system.");
            }

            settings.Set(settings.DOLPHIN_MANAGED, true);
            return Ok();
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    public void Deactivate() => settings.Set(settings.DOLPHIN_MANAGED, false);

    public void SyncSettings()
    {
        if (!IsActive || Kind is not (ManagedDolphinKind.Portable or ManagedDolphinKind.Bundled))
            return;

        var executable = Kind == ManagedDolphinKind.Portable ? FindExecutable() : null;
        if (Kind == ManagedDolphinKind.Portable && executable is null)
        {
            // The files are gone; leave the settings alone so onboarding or settings can pick another Dolphin.
            logger.LogWarning("The managed Dolphin is missing from {Folder}", AppFolderPath);
            return;
        }

        EnsureUserFolder();
        if (executable is not null)
        {
            var location = environment.IsWindows ? executable : ShellQuoting.QuoteUnixArgument(executable);
            if (settings.Get(settings.DOLPHIN_LOCATION) != location)
                settings.Set(settings.DOLPHIN_LOCATION, location);
        }

        if (!fileSystem.Path.PathsEqual(settings.Get(settings.USER_FOLDER_PATH), UserFolderPath))
            settings.Set(settings.USER_FOLDER_PATH, UserFolderPath);
    }

    public OperationResult ImportUserData(string sourceWiiFolderPath, string sourceConfigFolderPath)
    {
        try
        {
            // Called once the managed Dolphin is active, so its folders are whatever the settings now resolve to;
            // that also covers the Flatpak, whose config lives apart from its data.
            var userFolder = settings.Get(settings.USER_FOLDER_PATH);
            var wiiFolder = fileSystem.Path.Combine(userFolder, "Wii");
            var configFolder = dolphinPaths.Resolve(settings.Get(settings.DOLPHIN_LOCATION), userFolder).ConfigFolderPath;
            fileSystem.Directory.CreateDirectory(configFolder);

            if (fileSystem.Directory.Exists(sourceWiiFolderPath) && !fileSystem.Path.PathsEqual(sourceWiiFolderPath, wiiFolder))
                CopyDirectory(sourceWiiFolderPath, wiiFolder);

            if (fileSystem.Path.PathsEqual(sourceConfigFolderPath, configFolder))
                return Ok();
            foreach (var file in ImportedConfigFiles)
            {
                var source = fileSystem.Path.Combine(sourceConfigFolderPath, file);
                if (fileSystem.File.Exists(source))
                    fileSystem.File.Copy(source, fileSystem.Path.Combine(configFolder, file), overwrite: true);
            }

            var profiles = fileSystem.Path.Combine(sourceConfigFolderPath, "Profiles");
            if (fileSystem.Directory.Exists(profiles))
                CopyDirectory(profiles, fileSystem.Path.Combine(configFolder, "Profiles"));
            return Ok();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Importing Dolphin data failed");
            return Fail($"Wheel Wizard could not copy your Dolphin data: {exception.Message}");
        }
    }

    public async Task<DolphinRelease?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsActive || Kind is not (ManagedDolphinKind.Portable or ManagedDolphinKind.Flatpak))
            return null;

        var installed = await Task.Run(() => InstalledVersion, cancellationToken);
        if (string.IsNullOrWhiteSpace(installed))
            return null;

        var latest = await releases.GetLatestReleaseAsync(cancellationToken);
        if (latest.IsFailure)
            return null;
        return string.Equals(latest.Value.ShortRevision, installed, StringComparison.OrdinalIgnoreCase) ? null : latest.Value;
    }

    public async Task<OperationResult> UpdateAsync(
        IProgress<ManagedDolphinProgress>? progress = null,
        CancellationToken cancellationToken = default
    ) =>
        Kind switch
        {
            ManagedDolphinKind.Portable => await InstallPortableAsync(progress, cancellationToken) is { IsFailure: true } failure
                ? failure
                : Activate(),
            ManagedDolphinKind.Flatpak => await linuxInstaller.UpdateFlatpakDolphin(
                FlatpakAppId,
                new Progress<int>(percent => progress?.Report(new(t("onboarding.dolphin_updating"), percent)))
            ),
            _ => Fail("Wheel Wizard does not manage a Dolphin it can update here."),
        };

    public long GetInstalledSize()
    {
        try
        {
            return fileSystem.Directory.Exists(RootFolderPath)
                ? fileSystem
                    .Directory.EnumerateFiles(RootFolderPath, "*", SearchOption.AllDirectories)
                    .Sum(file => fileSystem.FileInfo.New(file).Length)
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    public bool CanRemove => !IsActive && !IsUsedByConfiguredDolphin();

    public OperationResult Remove()
    {
        if (IsActive)
            return Fail("Switch to another Dolphin before removing the managed one.");
        // Deleting the folder would take that Dolphin's saves and Miis with it.
        if (IsUsedByConfiguredDolphin())
            return Fail("Your Dolphin still uses the folder Wheel Wizard installed Dolphin into.");
        try
        {
            if (fileSystem.Directory.Exists(RootFolderPath))
                fileSystem.Directory.Delete(RootFolderPath, recursive: true);
            return Ok();
        }
        catch (Exception exception)
        {
            return Fail($"Wheel Wizard could not remove the managed Dolphin: {exception.Message}");
        }
    }

    private async Task<OperationResult> InstallPortableAsync(
        IProgress<ManagedDolphinProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        var downloadingText = t("onboarding.dolphin_downloading");
        progress?.Report(new(downloadingText, 0));
        var release = await releases.GetLatestReleaseAsync(cancellationToken);
        if (release.IsFailure)
            return release.Error;

        var artifact = releases.SelectArtifact(release.Value);
        if (artifact is null)
            return Fail("Dolphin does not publish a build for this system.");

        var stagingFolder = fileSystem.Path.Combine(RootFolderPath, "App.new");
        var archivePath = fileSystem.Path.Combine(DownloadFolderPath, fileSystem.Path.GetFileName(new Uri(artifact.Url).AbsolutePath));
        try
        {
            var download = await downloads.DownloadAsync(
                artifact.Url,
                archivePath,
                useExactPath: true,
                new Progress<DownloadProgress>(value => progress?.Report(new(downloadingText, value.Percentage * 7 / 10))),
                cancellationToken
            );
            if (download.IsFailure)
                return download.Error;

            progress?.Report(new(t("state.extracting"), 70));
            if (fileSystem.Directory.Exists(stagingFolder))
                fileSystem.Directory.Delete(stagingFolder, recursive: true);
            fileSystem.Directory.CreateDirectory(stagingFolder);

            var unpacked = await Task.Run(
                () =>
                    environment.IsWindows
                        ? Extract7z(
                            download.Value,
                            stagingFolder,
                            percent => progress?.Report(new(t("state.extracting"), 70 + percent / 4))
                        )
                        : CopyFromDiskImage(download.Value, stagingFolder),
                cancellationToken
            );
            if (unpacked.IsFailure)
                return unpacked;

            var swap = ReplaceAppFolder(stagingFolder);
            if (swap.IsFailure)
                return swap;

            fileSystem.File.WriteAllText(
                VersionFilePath,
                JsonSerializer.Serialize(new InstalledRelease(release.Value.ShortRevision, release.Value.Hash))
            );
            EnsureUserFolder();
            progress?.Report(new(t("onboarding.dolphin_ready"), 100));
            logger.LogInformation("Installed managed Dolphin {Version}", release.Value.ShortRevision);
            return Ok();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return Fail("The Dolphin download was cancelled.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Installing the managed Dolphin failed");
            return Fail($"Wheel Wizard could not install Dolphin: {exception.Message}");
        }
        finally
        {
            TryDeleteFolder(DownloadFolderPath);
            TryDeleteFolder(stagingFolder);
        }
    }

    private OperationResult Extract7z(string archivePath, string destination, Action<int> reportPercent)
    {
        using var stream = fileSystem.File.OpenRead(archivePath);
        using var archive = ArchiveFactory.OpenArchive(stream);
        var total = Math.Max(1, archive.Entries.Count(entry => !entry.IsDirectory));
        var done = 0;
        // Dolphin's archives are solid, so read them front to back instead of seeking per entry.
        using var reader = archive.ExtractAllEntries();
        while (reader.MoveToNextEntry())
        {
            var entry = reader.Entry;
            if (entry.IsDirectory)
                continue;
            if (!PathSafety.TryGetPathWithinDirectory(destination, entry.Key ?? string.Empty, out var target))
                return Fail("The Dolphin archive contains a file outside its folder.");

            var directory = fileSystem.Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
                fileSystem.Directory.CreateDirectory(directory);
            using (var input = reader.OpenEntryStream())
            using (var output = fileSystem.File.Create(target))
                input.CopyTo(output);
            reportPercent(++done * 100 / total);
        }

        return Ok();
    }

    /// <summary>Mounts the official .dmg read-only and copies Dolphin.app out with ditto, which keeps its signature intact.</summary>
    private OperationResult CopyFromDiskImage(string imagePath, string destination)
    {
        var mountPoint = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"wheelwizard-dolphin-{Guid.NewGuid():N}");
        fileSystem.Directory.CreateDirectory(mountPoint);
        var attach = processes.Run(
            "hdiutil",
            ["attach", "-nobrowse", "-readonly", "-noautoopen", "-mountpoint", mountPoint, imagePath],
            out _,
            out var attachError
        );
        if (attach.IsFailure || attach.Value != 0)
            return Fail($"The Dolphin disk image could not be opened. {attachError}".Trim());

        try
        {
            var app = fileSystem.Path.Combine(mountPoint, "Dolphin.app");
            if (!fileSystem.Directory.Exists(app))
                return Fail("The Dolphin disk image does not contain Dolphin.app.");

            var copy = processes.Run("ditto", [app, fileSystem.Path.Combine(destination, "Dolphin.app")], out _, out var copyError);
            return copy.IsSuccess && copy.Value == 0 ? Ok() : Fail($"Dolphin.app could not be copied. {copyError}".Trim());
        }
        finally
        {
            processes.Run("hdiutil", ["detach", mountPoint, "-force"], out _, out _);
            TryDeleteFolder(mountPoint);
        }
    }

    /// <summary>Swaps in the new build while keeping the user folder, which lives next to it.</summary>
    private OperationResult ReplaceAppFolder(string stagingFolder)
    {
        var previous = fileSystem.Path.Combine(RootFolderPath, "App.old");
        TryDeleteFolder(previous);
        try
        {
            if (fileSystem.Directory.Exists(AppFolderPath))
                fileSystem.Directory.Move(AppFolderPath, previous);
        }
        catch (IOException)
        {
            return Fail("Close Dolphin and try again; it is still running from the folder Wheel Wizard needs to update.");
        }

        try
        {
            fileSystem.Directory.Move(stagingFolder, AppFolderPath);
        }
        catch (Exception exception)
        {
            if (fileSystem.Directory.Exists(previous) && !fileSystem.Directory.Exists(AppFolderPath))
                fileSystem.Directory.Move(previous, AppFolderPath);
            return Fail($"The new Dolphin could not be put in place: {exception.Message}");
        }

        TryDeleteFolder(previous);
        return Ok();
    }

    /// <summary>
    /// The onboarding gives a Dolphin that never ran the managed user folder, and a user folder can also be picked
    /// by hand; either way the managed Dolphin is inactive while its folder is still in use.
    /// </summary>
    private bool IsUsedByConfiguredDolphin()
    {
        bool IsWithinRoot(string path) =>
            !string.IsNullOrWhiteSpace(path)
            && (fileSystem.Path.PathsEqual(path, RootFolderPath) || fileSystem.Path.IsDescendantPath(path, RootFolderPath));

        // macOS and Linux locations are shell-quoted.
        return IsWithinRoot(settings.Get(settings.USER_FOLDER_PATH))
            || IsWithinRoot(settings.Get(settings.DOLPHIN_LOCATION).Trim('\'', '"'));
    }

    private string? FindExecutable()
    {
        if (!fileSystem.Directory.Exists(AppFolderPath))
            return null;

        if (environment.IsMacOS)
        {
            var app = fileSystem.Path.Combine(AppFolderPath, "Dolphin.app", "Contents", "MacOS", "Dolphin");
            return fileSystem.File.Exists(app) ? app : null;
        }

        // The Windows archive wraps everything in a folder such as Dolphin-x64; take the shallowest Dolphin.exe.
        return fileSystem
            .Directory.EnumerateFiles(AppFolderPath, "Dolphin.exe", SearchOption.AllDirectories)
            .OrderBy(path => path.Length)
            .FirstOrDefault();
    }

    private void EnsureUserFolder()
    {
        fileSystem.Directory.CreateDirectory(UserFolderPath);
        fileSystem.Directory.CreateDirectory(fileSystem.Path.Combine(UserFolderPath, "Config"));
    }

    private void CopyDirectory(string source, string destination)
    {
        fileSystem.Directory.CreateDirectory(destination);
        foreach (var file in fileSystem.Directory.GetFiles(source))
            fileSystem.File.Copy(file, fileSystem.Path.Combine(destination, fileSystem.Path.GetFileName(file)), overwrite: true);
        foreach (var folder in fileSystem.Directory.GetDirectories(source))
            CopyDirectory(folder, fileSystem.Path.Combine(destination, fileSystem.Path.GetFileName(folder)));
    }

    private void TryDeleteFolder(string folder)
    {
        try
        {
            if (fileSystem.Directory.Exists(folder))
                fileSystem.Directory.Delete(folder, recursive: true);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove {Folder}", folder);
        }
    }

    private sealed record InstalledRelease(string ShortRevision, string Hash);
}
