using System.IO.Abstractions;
using WheelWizard.DiscImages;
using WheelWizard.Settings;

namespace WheelWizard.MiiRendering.FaceLibrary;

public interface IMiiFaceLibrary
{
    /// <summary>The Wii face library from the user's Mario Kart Wii game file.</summary>
    OperationResult<RflResource> Load();
}

/// <summary>Reads the face library from the game file once, and again only when the game file changes.</summary>
public sealed class MiiFaceLibrary(ISettingsManager settings, IDiscImageService discImages, IFileSystem fileSystem) : IMiiFaceLibrary
{
    private readonly object _lock = new();
    private (string Path, long Length, DateTime LastWrite, RflResource Resource)? _cache;

    public OperationResult<RflResource> Load()
    {
        var path = settings.Get<string>(settings.GAME_LOCATION);
        if (string.IsNullOrWhiteSpace(path))
            return Fail("Miis are drawn with the face library from Mario Kart Wii, but no game file is set.");

        lock (_lock)
        {
            var file = fileSystem.FileInfo.New(path);
            if (!file.Exists)
                return Fail($"Game file not found: {path}");
            if (_cache is { } cached && cached.Path == path && cached.Length == file.Length && cached.LastWrite == file.LastWriteTimeUtc)
                return cached.Resource;

            var archive = discImages.ReadFile(path, RflResource.DiscPath);
            if (archive.IsFailure)
                return archive.Error!;
            var resource = RflResource.FromArchive(archive.Value);
            if (resource.IsFailure)
                return resource.Error!;

            _cache = (path, file.Length, file.LastWriteTimeUtc, resource.Value);
            return resource.Value;
        }
    }
}
