using System.IO.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WheelWizard.DiscImages;
using WheelWizard.Dolphin.Paths;
using WheelWizard.GameFiles.Domain;
using WheelWizard.Models.Enums;
using WheelWizard.Settings;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Processes;
using WheelWizard.WiiManagement.GameLicense;

namespace WheelWizard.GameFiles;

public enum GameFilesState
{
    /// <summary>Extracted, and <c>main.dol</c> and <c>StaticR.rel</c> are what was extracted.</summary>
    Ready,

    /// <summary>No game has ever been picked (a first run, which the onboarding handles).</summary>
    NoImage,

    /// <summary>An image is picked and reachable, but nothing is extracted yet.</summary>
    NeedsExtraction,

    /// <summary>An image is picked but cannot be read right now (moved, deleted, a drive that is not plugged in).</summary>
    ImageMissing,

    /// <summary>Something is where the extracted game goes that Wheel Wizard did not finish writing.</summary>
    UnknownFolder,

    /// <summary>Extracted, but <c>main.dol</c> or <c>StaticR.rel</c> changed or disappeared since.</summary>
    CodeModified,
}

public sealed record GameFilesStatus(GameFilesState State, GameFilesManifest? Manifest, string? ImagePath)
{
    public bool IsReady => State == GameFilesState.Ready;
}

public enum GameChangeKind
{
    /// <summary>Nothing is extracted yet.</summary>
    Extract,

    /// <summary>The same game and revision is already extracted; only the remembered image changes.</summary>
    SameGame,

    /// <summary>A different game or revision is extracted; replacing it deletes every change made to it.</summary>
    Replace,

    /// <summary>A folder Wheel Wizard does not know is in the way and has to be moved aside or deleted first.</summary>
    UnknownFolder,
}

public sealed record GameChangePlan(GameChangeKind Kind, DiscContents Image, GameFilesManifest? Current);

public sealed record GameExtractionProgress(double Fraction, string Message);

public interface IGameFilesService
{
    GameFilesStatus GetStatus();

    /// <summary>The picked image, also when it is not reachable right now.</summary>
    string? ImagePath { get; }

    /// <summary>
    /// Checks a game image without changing anything: whether it is Mario Kart Wii, and what picking it would do to
    /// the extracted game.
    /// </summary>
    OperationResult<GameChangePlan> Plan(string imagePath);

    /// <summary>
    /// Extracts <paramref name="imagePath"/> and makes it the game, then remembers the image. Never writes to, moves
    /// or deletes the image.
    /// </summary>
    /// <param name="replaceExisting">
    /// Required to replace an extraction of a different game or revision, which deletes it with every change made to it.
    /// </param>
    Task<OperationResult> ExtractAsync(
        string imagePath,
        bool replaceExisting,
        IProgress<GameExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Restores <c>main.dol</c> and <c>StaticR.rel</c> from the image of the same game; nothing else is touched.</summary>
    Task<OperationResult> RestoreCodeAsync(string? imagePath = null, CancellationToken cancellationToken = default);

    /// <summary>Renames whatever is in the way to <c>GAME.backup-…</c>. A link is renamed, never followed.</summary>
    OperationResult<string> MoveUnknownAside();

    /// <summary>Deletes whatever is in the way. A link is removed without touching what it points to.</summary>
    OperationResult DeleteUnknown();

    /// <summary>
    /// Finishes or rolls back work a previous run was interrupted in, and removes Wheel Wizard's own leftovers.
    /// Only ever deletes folders that carry its work marker.
    /// </summary>
    void RecoverInterruptedWork();

    /// <summary>Dolphin or WiiCompiled has files of the game open, which blocks replacing them on Windows.</summary>
    bool IsGameRunning();

    void CloseRunningGame();

    /// <summary>The Mario Kart Wii region of the extracted game, or of the image when nothing is extracted yet.</summary>
    MarioKartWiiEnums.Regions GetRegion();
}

public sealed class GameFilesService(
    IGameFilesPaths paths,
    IDiscExtractor extractor,
    ISettingsManager settings,
    IFileSystem fileSystem,
    IProcessLauncher processes,
    IDolphinPaths dolphinPaths,
    IDiscImageService discImages,
    ILogger<GameFilesService> logger
) : IGameFilesService
{
    private const long FreeSpaceMargin = 256L * 1024 * 1024;

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    // The two hashes are checked on every status read; rehashing ~7 MB only when the files change keeps that cheap.
    private readonly Dictionary<string, (long Length, DateTime LastWrite, string Sha)> _hashCache = new();
    private readonly object _hashLock = new();
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string? ImagePath
    {
        get
        {
            var value = settings.Get(settings.GAME_LOCATION);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
            return settings.GAME_LOCATION.UnavailableValue is { } unavailable && !string.IsNullOrWhiteSpace(unavailable)
                ? unavailable
                : null;
        }
    }

    #region Status

    public GameFilesStatus GetStatus()
    {
        var image = ImagePath;
        var manifest = ReadManifest();
        if (manifest is not null)
            return new(CodeMatches(manifest) ? GameFilesState.Ready : GameFilesState.CodeModified, manifest, image);

        if (RootExists())
            return new(GameFilesState.UnknownFolder, null, image);
        if (image is null)
            return new(GameFilesState.NoImage, null, null);
        return new(IsReadableFile(image) ? GameFilesState.NeedsExtraction : GameFilesState.ImageMissing, null, image);
    }

    /// <summary>
    /// Whether something is where the extracted game goes. An empty folder, or a link to one (made to put the game on
    /// another drive), is just a place to extract into. A link whose target is not there (an unplugged drive) is not:
    /// extracting would create the folder somewhere the user did not mean.
    /// </summary>
    private bool RootExists()
    {
        try
        {
            var root = paths.RootPath;
            if (fileSystem.Directory.Exists(root))
                return fileSystem.Directory.EnumerateFileSystemEntries(root).Any();
            return fileSystem.File.Exists(root) || fileSystem.IsSymlink(root);
        }
        catch
        {
            // Unreadable is not empty.
            return true;
        }
    }

    private bool IsReadableFile(string path)
    {
        try
        {
            return fileSystem.File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private GameFilesManifest? ReadManifest() => ReadManifest(paths.ManifestPath);

    private GameFilesManifest? ReadManifest(string manifestPath)
    {
        try
        {
            if (!fileSystem.File.Exists(manifestPath))
                return null;
            var manifest = JsonSerializer.Deserialize<GameFilesManifest>(fileSystem.File.ReadAllText(manifestPath));
            return manifest is { SchemaVersion: GameFilesManifest.CurrentSchemaVersion } && !string.IsNullOrWhiteSpace(manifest.GameId)
                ? manifest
                : null;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The extracted game's manifest could not be read");
            return null;
        }
    }

    private void WriteManifest(string manifestPath, GameFilesManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, ManifestJson);
        // Written in place of nothing, flushed, then renamed: a manifest that exists is always complete.
        var temp = manifestPath + ".tmp";
        using (var stream = fileSystem.FileStream.New(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        fileSystem.File.Move(temp, manifestPath, overwrite: true);
    }

    private bool CodeMatches(GameFilesManifest manifest) =>
        HashEquals(HashFile(paths.MainDolPath), manifest.MainDolSha256)
        && HashEquals(HashFile(paths.StaticRelPath), manifest.StaticRelSha256);

    private static bool HashEquals(string? actual, string expected) =>
        actual is not null && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private string? HashFile(string path)
    {
        try
        {
            var file = fileSystem.FileInfo.New(path);
            if (!file.Exists)
                return null;
            lock (_hashLock)
            {
                if (
                    _hashCache.TryGetValue(path, out var cached)
                    && cached.Length == file.Length
                    && cached.LastWrite == file.LastWriteTimeUtc
                )
                    return cached.Sha;
            }

            using var stream = fileSystem.File.OpenRead(path);
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            lock (_hashLock)
                _hashCache[path] = (file.Length, file.LastWriteTimeUtc, sha);
            return sha;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not hash {Path}", path);
            return null;
        }
    }

    public MarioKartWiiEnums.Regions GetRegion()
    {
        var manifest = ReadManifest();
        if (manifest is not null)
            return GameRegion.FromGameId(manifest.GameId);
        // The license loader asks on every poll; the disc image service only reopens the image once it changes.
        var image = ImagePath;
        if (image is null)
            return MarioKartWiiEnums.Regions.None;
        var info = discImages.ReadInfo(image);
        return info.IsSuccess ? GameRegion.FromGameId(info.Value.GameId) : MarioKartWiiEnums.Regions.None;
    }

    #endregion

    #region Planning

    public OperationResult<GameChangePlan> Plan(string imagePath)
    {
        var contents = Inspect(imagePath);
        if (contents.IsFailure)
            return contents.Error;

        var manifest = ReadManifest();
        if (manifest is not null)
            return new GameChangePlan(
                manifest.IsSameGame(contents.Value.GameId, contents.Value.Revision) ? GameChangeKind.SameGame : GameChangeKind.Replace,
                contents.Value,
                manifest
            );

        return new GameChangePlan(RootExists() ? GameChangeKind.UnknownFolder : GameChangeKind.Extract, contents.Value, null);
    }

    private OperationResult<DiscContents> Inspect(string imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !IsReadableFile(imagePath))
            return Fail(t("message_error.invalid_game_file.unreadable"));
        if (IsInsideGameFiles(imagePath))
            return Fail(t("game_files.image_inside_game_folder"));

        var contents = extractor.Inspect(imagePath);
        if (contents.IsFailure)
            return Fail(t("message_error.invalid_game_file.unreadable"));
        if (GameRegion.FromGameId(contents.Value.GameId) == MarioKartWiiEnums.Regions.None)
            return Fail(
                t("message_error.invalid_game_file.wrong_game", new { name = contents.Value.InternalName, id = contents.Value.GameId })
            );
        return contents;
    }

    /// <summary>An image kept inside the extracted game would be deleted along with it on the next replace.</summary>
    private bool IsInsideGameFiles(string imagePath)
    {
        try
        {
            var full = fileSystem.Path.GetFullPath(imagePath);
            return new[] { paths.RootPath, paths.ResolvedRootPath, paths.StagingPath, paths.ReplacedPath }.Any(folder =>
                fileSystem.Path.IsDescendantPath(full, folder)
            );
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Extraction

    public async Task<OperationResult> ExtractAsync(
        string imagePath,
        bool replaceExisting,
        IProgress<GameExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return Fail(t("game_files.busy"));
        try
        {
            return await Task.Run(() => ExtractCore(imagePath, replaceExisting, progress, cancellationToken), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private OperationResult ExtractCore(
        string imagePath,
        bool replaceExisting,
        IProgress<GameExtractionProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        RecoverInterruptedWork();
        var planResult = Plan(imagePath);
        if (planResult.IsFailure)
            return planResult.Error;
        var plan = planResult.Value;

        switch (plan.Kind)
        {
            case GameChangeKind.UnknownFolder:
                return Fail(t("game_files.unknown_folder_body"));
            case GameChangeKind.Replace when !replaceExisting:
                return Fail(t("game_files.replace_not_confirmed"));
            case GameChangeKind.SameGame:
                return RememberImage(imagePath, plan.Current!);
        }

        var root = paths.ResolvedRootPath;
        if (fileSystem.Path.IsRootDirectory(root))
            return Fail(t("game_files.link_to_drive_root"));
        if (IsGameRunning())
            return Fail(t("game_files.close_game_body"));

        var staging = paths.StagingPath;
        var space = EnsureFreeSpace(fileSystem.Path.GetDirectoryName(staging) ?? staging, plan.Image.RequiredBytes);
        if (space.IsFailure)
            return space;

        try
        {
            DeleteWorkFolder(staging);
            fileSystem.Directory.CreateDirectory(staging);
            fileSystem.File.WriteAllText(fileSystem.Path.Combine(staging, GameFilesPaths.WorkMarkerFileName), "");

            progress?.Report(new(0, t("game_files.extracting")));
            var extracted = extractor.Extract(
                imagePath,
                staging,
                new InlineProgress<double>(value => progress?.Report(new(value, t("game_files.extracting")))),
                cancellationToken
            );
            if (extracted.IsFailure)
            {
                DeleteWorkFolder(staging);
                return extracted.Error;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var image = fileSystem.FileInfo.New(imagePath);
            WriteManifest(
                fileSystem.Path.Combine(staging, GameFilesPaths.ManifestFileName),
                new()
                {
                    GameId = plan.Image.GameId,
                    Revision = plan.Image.Revision,
                    InternalName = plan.Image.InternalName,
                    SourcePath = fileSystem.Path.GetFullPath(imagePath),
                    SourceLength = image.Length,
                    SourceLastWriteUtc = image.LastWriteTimeUtc,
                    MainDolSha256 = extracted.Value.MainDolSha256,
                    StaticRelSha256 = extracted.Value.StaticRelSha256,
                    ExtractorVersion = extractor.Version,
                    ExtractedUtc = DateTime.UtcNow,
                }
            );

            progress?.Report(new(1, t("game_files.finishing")));
            Promote();
        }
        catch (OperationCanceledException)
        {
            TryDeleteWorkFolder(staging);
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Extracting the game failed");
            // A failure before the swap leaves only the staging folder; a failure during it is finished on the next start.
            if (ReadManifest(fileSystem.Path.Combine(staging, GameFilesPaths.ManifestFileName)) is null)
                TryDeleteWorkFolder(staging);
            return new OperationError { Message = $"{t("game_files.extract_failed")} {exception.Message}", Exception = exception };
        }

        return settings.Set(settings.GAME_LOCATION, imagePath) ? Ok() : Fail(t("message_error.invalid_game_file.unreadable"));
    }

    private OperationResult RememberImage(string imagePath, GameFilesManifest current)
    {
        try
        {
            var image = fileSystem.FileInfo.New(imagePath);
            WriteManifest(
                paths.ManifestPath,
                current with
                {
                    SourcePath = fileSystem.Path.GetFullPath(imagePath),
                    SourceLength = image.Length,
                    SourceLastWriteUtc = image.LastWriteTimeUtc,
                }
            );
        }
        catch (Exception exception)
        {
            // The manifest only remembers the image for information; the game itself is fine as it is.
            logger.LogWarning(exception, "Could not record the new game image in the manifest");
        }

        return settings.Set(settings.GAME_LOCATION, imagePath) ? Ok() : Fail(t("message_error.invalid_game_file.unreadable"));
    }

    private OperationResult EnsureFreeSpace(string folder, long required)
    {
        try
        {
            fileSystem.Directory.CreateDirectory(folder);
            var drive = fileSystem.DriveInfo.New(fileSystem.Path.GetPathRoot(fileSystem.Path.GetFullPath(folder))!);
            var needed = required + FreeSpaceMargin;
            if (drive.AvailableFreeSpace >= needed)
                return Ok();
            return Fail(
                t("game_files.not_enough_space", new { needed = FormatSize(needed), available = FormatSize(drive.AvailableFreeSpace) })
            );
        }
        catch (Exception exception)
        {
            // Some drives (network shares, some Linux mounts) can't say; the extraction itself still stops on a full disk.
            logger.LogWarning(exception, "Could not check the free space for the game files");
            return Ok();
        }
    }

    private static string FormatSize(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";

    /// <summary>The top-level entries of an extraction. Anything else the user put in the folder is never moved or deleted.</summary>
    private static readonly string[] LayoutEntries = ["DATA", "disc"];

    /// <summary>
    /// Swaps the finished staging folder in. The current manifest leaves first and the new one arrives last, so at
    /// every point in between <see cref="RecoverInterruptedWork"/> can tell which half is old and which is new.
    /// </summary>
    private void Promote()
    {
        var root = paths.ResolvedRootPath;
        var staging = paths.StagingPath;
        var replaced = paths.ReplacedPath;
        fileSystem.Directory.CreateDirectory(root);

        var currentManifest = fileSystem.Path.Combine(root, GameFilesPaths.ManifestFileName);
        if (fileSystem.File.Exists(currentManifest))
        {
            EnsureWorkFolder(replaced);
            fileSystem.File.Move(currentManifest, fileSystem.Path.Combine(replaced, GameFilesPaths.ManifestFileName), overwrite: true);
        }

        FinishPromote();
    }

    private void FinishPromote()
    {
        var root = paths.ResolvedRootPath;
        var staging = paths.StagingPath;
        var replaced = paths.ReplacedPath;

        foreach (var entry in LayoutEntries)
        {
            var incoming = fileSystem.Path.Combine(staging, entry);
            if (!fileSystem.Directory.Exists(incoming))
                continue;

            var current = fileSystem.Path.Combine(root, entry);
            if (fileSystem.Directory.Exists(current) || fileSystem.File.Exists(current))
            {
                EnsureWorkFolder(replaced);
                var parked = fileSystem.Path.Combine(replaced, entry);
                if (fileSystem.Directory.Exists(parked))
                    DeleteTree(parked);
                MoveEntry(current, parked);
            }

            fileSystem.Directory.Move(incoming, current);
        }

        fileSystem.File.Move(
            fileSystem.Path.Combine(staging, GameFilesPaths.ManifestFileName),
            fileSystem.Path.Combine(root, GameFilesPaths.ManifestFileName),
            overwrite: true
        );
        lock (_hashLock)
            _hashCache.Clear();

        TryDeleteWorkFolder(replaced);
        TryDeleteWorkFolder(staging);
    }

    private void MoveEntry(string from, string to)
    {
        if (fileSystem.File.Exists(from) && !fileSystem.Directory.Exists(from))
            fileSystem.File.Move(from, to);
        else
            fileSystem.Directory.Move(from, to);
    }

    public void RecoverInterruptedWork()
    {
        try
        {
            var staging = paths.StagingPath;
            var stagingManifest = fileSystem.Path.Combine(staging, GameFilesPaths.ManifestFileName);
            if (IsWorkFolder(staging) && ReadManifest(stagingManifest) is not null && ReadManifest() is null)
            {
                // The swap had started: the new extraction is complete, the old manifest already left.
                logger.LogInformation("Finishing an interrupted game extraction");
                FinishPromote();
                if (ReadManifest() is { } finished && IsReadableFile(finished.SourcePath))
                    settings.Set(settings.GAME_LOCATION, finished.SourcePath);
                return;
            }

            TryDeleteWorkFolder(staging);
            if (ReadManifest() is not null)
                TryDeleteWorkFolder(paths.ReplacedPath);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clean up an interrupted game extraction");
        }
    }

    #endregion

    #region Restore

    public async Task<OperationResult> RestoreCodeAsync(string? imagePath = null, CancellationToken cancellationToken = default)
    {
        if (!await _gate.WaitAsync(0, cancellationToken))
            return Fail(t("game_files.busy"));
        try
        {
            return await Task.Run(() => RestoreCodeCore(imagePath ?? ImagePath, cancellationToken), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private OperationResult RestoreCodeCore(string? imagePath, CancellationToken cancellationToken)
    {
        var manifest = ReadManifest();
        if (manifest is null)
            return Fail(t("game_files.not_extracted"));
        if (imagePath is null)
            return Fail(t("message_error.invalid_game_file.unreadable"));

        var contents = Inspect(imagePath);
        if (contents.IsFailure)
            return contents.Error;
        if (!manifest.IsSameGame(contents.Value.GameId, contents.Value.Revision))
            return Fail(t("game_files.restore_wrong_game", new { id = manifest.GameId }));
        if (IsGameRunning())
            return Fail(t("game_files.close_game_body"));

        var restored = extractor.RestoreCode(imagePath, fileSystem.Path.Combine(paths.ResolvedRootPath, "DATA"), cancellationToken);
        if (restored.IsFailure)
            return restored.Error;

        lock (_hashLock)
            _hashCache.Clear();
        try
        {
            var image = fileSystem.FileInfo.New(imagePath);
            WriteManifest(
                paths.ManifestPath,
                manifest with
                {
                    MainDolSha256 = restored.Value.MainDolSha256,
                    StaticRelSha256 = restored.Value.StaticRelSha256,
                    SourcePath = fileSystem.Path.GetFullPath(imagePath),
                    SourceLength = image.Length,
                    SourceLastWriteUtc = image.LastWriteTimeUtc,
                }
            );
        }
        catch (Exception exception)
        {
            return new OperationError { Message = $"{t("game_files.extract_failed")} {exception.Message}", Exception = exception };
        }

        return settings.Set(settings.GAME_LOCATION, imagePath) ? Ok() : Fail(t("message_error.invalid_game_file.unreadable"));
    }

    #endregion

    #region Unknown folder

    public OperationResult<string> MoveUnknownAside()
    {
        if (ReadManifest() is not null)
            return Fail(t("game_files.not_unknown"));
        try
        {
            var root = paths.RootPath;
            var backup = $"{root}.backup-{DateTime.Now:yyyyMMdd-HHmmss}";
            for (var attempt = 2; fileSystem.Directory.Exists(backup) || fileSystem.File.Exists(backup); attempt++)
                backup = $"{root}.backup-{DateTime.Now:yyyyMMdd-HHmmss}-{attempt}";

            // Moving the link itself, not its target: the folder it points to stays where the user put it.
            if (fileSystem.IsSymlink(root))
                MoveLink(root, backup);
            else if (fileSystem.File.Exists(root) && !fileSystem.Directory.Exists(root))
                fileSystem.File.Move(root, backup);
            else
                fileSystem.Directory.Move(root, backup);
            logger.LogInformation("Moved an unknown game folder aside to {Backup}", backup);
            return backup;
        }
        catch (Exception exception)
        {
            return new OperationError { Message = $"{t("game_files.move_aside_failed")} {exception.Message}", Exception = exception };
        }
    }

    public OperationResult DeleteUnknown()
    {
        if (ReadManifest() is not null)
            return Fail(t("game_files.not_unknown"));
        try
        {
            var root = paths.RootPath;
            if (fileSystem.IsSymlink(root))
                DeleteLink(root);
            else if (fileSystem.Directory.Exists(root))
                DeleteTree(root);
            else if (fileSystem.File.Exists(root))
                fileSystem.File.Delete(root);
            logger.LogInformation("Deleted an unknown game folder at {Root}", root);
            return Ok();
        }
        catch (Exception exception)
        {
            return new OperationError { Message = $"{t("game_files.delete_failed")} {exception.Message}", Exception = exception };
        }
    }

    /// <summary>Recreates the link under the new name and removes the old one, so nothing behind it moves.</summary>
    private void MoveLink(string from, string to)
    {
        var target = fileSystem.FileInfo.New(from).LinkTarget!;
        try
        {
            fileSystem.Directory.CreateSymbolicLink(to, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
        {
            // Windows only lets some accounts create links, but renaming one (a junction too) moves just the link.
            fileSystem.Directory.Move(from, to);
            return;
        }

        DeleteLink(from);
    }

    private void DeleteLink(string path)
    {
        // A directory link (symlink or junction) is removed as a directory on Windows and unlinked like a file
        // elsewhere; neither touches its target.
        if (fileSystem.Path.DirectorySeparatorChar == '\\' && fileSystem.Directory.Exists(path))
            fileSystem.Directory.Delete(path, recursive: false);
        else
            fileSystem.File.Delete(path);
    }

    #endregion

    #region Deleting

    private bool IsWorkFolder(string path)
    {
        try
        {
            return fileSystem.Directory.Exists(path)
                && !fileSystem.IsSymlink(path)
                && fileSystem.File.Exists(fileSystem.Path.Combine(path, GameFilesPaths.WorkMarkerFileName));
        }
        catch
        {
            return false;
        }
    }

    private void EnsureWorkFolder(string path)
    {
        if (IsWorkFolder(path))
            return;
        if (fileSystem.Directory.Exists(path) || fileSystem.File.Exists(path))
            throw new IOException($"'{path}' is in the way and is not Wheel Wizard's to remove.");
        fileSystem.Directory.CreateDirectory(path);
        fileSystem.File.WriteAllText(fileSystem.Path.Combine(path, GameFilesPaths.WorkMarkerFileName), "");
    }

    private void DeleteWorkFolder(string path)
    {
        if (!fileSystem.Directory.Exists(path) && !fileSystem.File.Exists(path))
            return;
        if (!IsWorkFolder(path))
            throw new IOException($"'{path}' is in the way and is not Wheel Wizard's to remove.");
        DeleteTree(path);
    }

    private void TryDeleteWorkFolder(string path)
    {
        try
        {
            if (IsWorkFolder(path))
                DeleteTree(path);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not delete {Path}; it is retried on the next start", path);
        }
    }

    /// <summary>Deletes a folder without ever following a link inside it: links are removed, their targets are left alone.</summary>
    private void DeleteTree(string path)
    {
        foreach (var entry in fileSystem.Directory.EnumerateFileSystemEntries(path))
        {
            if (fileSystem.IsSymlink(entry))
            {
                DeleteLink(entry);
                continue;
            }

            if (fileSystem.Directory.Exists(entry))
            {
                DeleteTree(entry);
                continue;
            }

            var file = fileSystem.FileInfo.New(entry);
            if (file.IsReadOnly)
                file.IsReadOnly = false;
            file.Delete();
        }

        fileSystem.Directory.Delete(path, recursive: false);
    }

    #endregion

    #region Running game

    // Dolphin's process name on each platform and packaging, and WiiCompiled's two runtimes.
    private static readonly string[] GameProcessNames = ["Dolphin", "dolphin-emu", "DolphinQt", "WiiCompiled", "RetroRewind"];

    public bool IsGameRunning() => RunningProcessNames().Any(name => processes.GetProcessIds(name).Count > 0);

    public void CloseRunningGame()
    {
        foreach (var name in RunningProcessNames())
        {
            try
            {
                processes.KillByName(name);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not close {Process}", name);
            }
        }
    }

    private IEnumerable<string> RunningProcessNames()
    {
        var names = new HashSet<string>(GameProcessNames, StringComparer.OrdinalIgnoreCase);
        try
        {
            var configured = fileSystem.Path.GetFileNameWithoutExtension(dolphinPaths.ExecutablePath);
            if (!string.IsNullOrWhiteSpace(configured) && !configured.Contains(' '))
                names.Add(configured);
        }
        catch
        {
            // A Dolphin command that isn't a path (a Flatpak command line) is covered by the names above.
        }

        return names;
    }

    #endregion

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
