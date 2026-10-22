using System.IO.Abstractions;
using RVZSharp.Blobs;
using RVZSharp.Files;
using RVZSharp.Interfaces;
using RVZSharp.Wii;

namespace WheelWizard.CustomMusic.Library;

/// <summary>
/// Reads the original BRSTM files from the player's own Mario Kart Wii disc image (ISO, WBFS, RVZ, ...),
/// so replacements can be matched to the loudness of the exact music they replace.
/// </summary>
public interface IGameDiscMusicReader
{
    /// <summary>The bytes of /sound/strm/<paramref name="fileName"/>, or an error saying why the disc could not provide it.</summary>
    OperationResult<byte[]> ReadStreamFile(string gamePath, string fileName);
}

public sealed class GameDiscMusicReader(IFileSystem fileSystem) : IGameDiscMusicReader
{
    private const uint DataPartitionType = 0;

    public OperationResult<byte[]> ReadStreamFile(string gamePath, string fileName)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return new OperationError { Message = t("custom_music.disc.not_set") };
        if (!fileSystem.File.Exists(gamePath))
            return new OperationError { Message = t("custom_music.disc.missing") };

        Stream? stream = null;
        IBlobReader? disc = null;
        try
        {
            stream = fileSystem.File.OpenRead(gamePath);
            disc = Blob.Open(stream, gamePath, leaveOpen: false);
            stream = null; // the blob reader owns it now

            var partitions = WiiVolume.GetPartitions(disc);
            using var files = partitions.Count == 0
                ? DiscFileSystem.Open(disc)
                : DiscFileSystem.Open(disc, partitions.FirstOrDefault(p => p.Type == DataPartitionType, partitions[0]));

            var entry = files.Find($"sound/strm/{fileName}");
            if (entry == null || entry.IsDirectory || entry.Size <= 0 || entry.Size > 64 * 1024 * 1024)
                return new OperationError { Message = t("custom_music.disc.no_file", new { file = fileName }) };

            using var output = new MemoryStream((int)entry.Size);
            files.CopyFileTo(entry, output);
            return output.ToArray();
        }
        catch (Exception exception)
        {
            // Unsupported, encrypted with unknown keys, or damaged images: the caller falls back to an estimate.
            return new OperationError { Message = t("custom_music.disc.unreadable", new { message = exception.Message }), Exception = exception };
        }
        finally
        {
            disc?.Dispose();
            stream?.Dispose();
        }
    }
}
