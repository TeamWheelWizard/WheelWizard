using System.IO.Abstractions;
using RVZSharp.Blobs;
using RVZSharp.Files;
using RVZSharp.Interfaces;
using RVZSharp.Models;
using RVZSharp.Wii;
using WheelWizard.DiscImages.Domain;

namespace WheelWizard.DiscImages;

public interface IDiscImageService
{
    /// <summary>File extensions (with dot) of the disc image formats a Wii game can be read from.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Reads the disc header of a game file in any supported format.</summary>
    OperationResult<DiscImageInfo> ReadInfo(string path);

    /// <summary>Reads one file (e.g. <c>contents/RFLRes01.arc</c>) from the game partition of a Wii game file.</summary>
    OperationResult<byte[]> ReadFile(string path, string discPath);
}

public sealed class DiscImageService(IFileSystem fileSystem) : IDiscImageService
{
    private const uint GamePartitionType = 0;

    private readonly object _cacheLock = new();
    private (string Path, long Length, DateTime LastWrite, DiscImageInfo Info)? _cache;

    public IReadOnlyList<string> SupportedExtensions { get; } = Enum.GetValues<BlobType>().SelectMany(ExtensionsFor).ToArray();

    private static string[] ExtensionsFor(BlobType type) =>
        type switch
        {
            BlobType.Plain => [".iso", ".gcm"],
            BlobType.Gcz => [".gcz"],
            BlobType.Ciso => [".ciso"],
            BlobType.Wbfs => [".wbfs"],
            BlobType.Wia => [".wia"],
            BlobType.Rvz => [".rvz"],
            // TGC only wraps GameCube discs, and NFS needs key files from a Wii U title next to it.
            _ => [],
        };

    public OperationResult<DiscImageInfo> ReadInfo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Fail("No game file is set.");

        try
        {
            var file = fileSystem.FileInfo.New(path);
            if (!file.Exists)
                return Fail($"Game file not found: {path}");

            // The license loader asks on every poll, so only open the image again once the file changes.
            lock (_cacheLock)
            {
                if (
                    _cache is { } cached
                    && cached.Path == path
                    && cached.Length == file.Length
                    && cached.LastWrite == file.LastWriteTimeUtc
                )
                    return cached.Info;
            }

            var result = Read(path);
            // Failures aren't cached: a file that was briefly locked should read fine on the next call.
            if (result.IsSuccess)
            {
                lock (_cacheLock)
                    _cache = (path, file.Length, file.LastWriteTimeUtc, result.Value);
            }
            return result;
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to read game file: {ex.Message}", Exception = ex };
        }
    }

    public OperationResult<byte[]> ReadFile(string path, string discPath)
    {
        if (string.IsNullOrWhiteSpace(path))
            return Fail("No game file is set.");

        try
        {
            if (!fileSystem.File.Exists(path))
                return Fail($"Game file not found: {path}");

            using var reader = Open(path);
            if (!WiiVolume.IsWiiDisc(reader))
                return Fail("This file is not a Wii disc image.");
            var game = WiiVolume.GetPartitions(reader).Where(partition => partition.Type == GamePartitionType).ToList();
            if (game.Count == 0)
                return Fail("This disc has no game partition.");

            using var files = DiscFileSystem.Open(reader, game[0]);
            var file = files.Find(discPath);
            if (file == null || file.IsDirectory)
                return Fail($"The game file has no {discPath}.");

            using var bytes = new MemoryStream();
            files.CopyFileTo(file, bytes);
            return bytes.ToArray();
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to read {discPath} from the game file: {ex.Message}", Exception = ex };
        }
    }

    private IBlobReader Open(string path)
    {
        // Read-only and shared: the image stays usable by anything else that has it open, and is never written.
        var stream = fileSystem.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return Blob.Open(stream, path, leaveOpen: false);
        }
        catch
        {
            // Blob.Open only takes ownership of the stream once it succeeds.
            stream.Dispose();
            throw;
        }
    }

    private OperationResult<DiscImageInfo> Read(string path)
    {
        try
        {
            using var reader = Open(path);
            var info = DiscInfo.TryRead(reader);
            if (info == null)
                return Fail("This file is not a GameCube or Wii disc image.");

            return new DiscImageInfo(info.GameId, info.InternalName, info.Revision, ToFormat(reader.Type));
        }
        catch (Exception ex)
        {
            return new OperationError { Message = $"Failed to read game file: {ex.Message}", Exception = ex };
        }
    }

    private static DiscImageFormat ToFormat(BlobType type) =>
        type switch
        {
            BlobType.Gcz => DiscImageFormat.Gcz,
            BlobType.Ciso => DiscImageFormat.Ciso,
            BlobType.Wbfs => DiscImageFormat.Wbfs,
            BlobType.Tgc => DiscImageFormat.Tgc,
            BlobType.Wia => DiscImageFormat.Wia,
            BlobType.Rvz => DiscImageFormat.Rvz,
            BlobType.Nfs => DiscImageFormat.Nfs,
            _ => DiscImageFormat.Iso,
        };
}
