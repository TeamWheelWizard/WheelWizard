using System.Buffers.Binary;
using System.IO.Abstractions;
using System.Security.Cryptography;
using RVZSharp.Blobs;
using RVZSharp.Files;
using RVZSharp.Interfaces;
using RVZSharp.Models;
using RVZSharp.Wii;

namespace WheelWizard.GameFiles;

/// <summary>What a disc image holds, read from its header and file system table without extracting anything.</summary>
public sealed record DiscContents(string GameId, byte Revision, string InternalName, long RequiredBytes);

/// <summary>The two files whose hashes a finished extraction is pinned to.</summary>
public sealed record ExtractedCode(string MainDolSha256, string StaticRelSha256);

public interface IDiscExtractor
{
    /// <summary>Bumped whenever the extracted layout changes.</summary>
    int Version { get; }

    OperationResult<DiscContents> Inspect(string imagePath);

    /// <summary>
    /// Writes the game partition of a Wii disc image into an empty <paramref name="destinationRoot"/>, in the layout
    /// Dolphin's "Extract Entire Disc" uses: <c>disc/</c> plus <c>DATA/{sys,files,ticket.bin,tmd.bin,cert.bin,h3.bin}</c>.
    /// Only reads the image.
    /// </summary>
    OperationResult<ExtractedCode> Extract(
        string imagePath,
        string destinationRoot,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    );

    /// <summary>Writes only <c>main.dol</c> and <c>StaticR.rel</c> again, replacing whatever is in <paramref name="dataPath"/>.</summary>
    OperationResult<ExtractedCode> RestoreCode(string imagePath, string dataPath, CancellationToken cancellationToken = default);
}

public sealed class DiscExtractor(IFileSystem fileSystem) : IDiscExtractor
{
    private const int TicketSize = 0x2A4;
    private const int H3Size = 0x18000;
    private const long RegionOffset = 0x4E000;
    private const int RegionSize = 0x20;
    private const int DiscHeaderSize = 0x100;
    private const int BootSize = 0x440;
    private const int Bi2Size = 0x2000;
    private const long ApploaderOffset = 0x2440;
    private const uint GamePartitionType = 0;
    private const string StaticRelDiscPath = "rel/StaticR.rel";

    // Room for file system overhead (clusters, directory entries) on top of the bytes themselves.
    private const long PerFileOverhead = 4096;

    public int Version => 1;

    public OperationResult<DiscContents> Inspect(string imagePath)
    {
        try
        {
            using var disc = OpenDisc(imagePath);
            var info = DiscInfo.TryRead(disc.Reader);
            if (info is null)
                return Fail("This file is not a Wii disc image.");

            using var files = DiscFileSystem.Open(disc.Reader, disc.Game);
            var fileBytes = 0L;
            var count = 0;
            foreach (var file in EnumerateFiles(files.Root))
            {
                fileBytes += file.Size;
                count++;
            }

            // The system files are a few MB at most; counting them generously keeps the estimate on the safe side.
            var required = fileBytes + (count + 16) * PerFileOverhead + 16L * 1024 * 1024;
            return new DiscContents(info.GameId, info.Revision, info.InternalName, required);
        }
        catch (Exception exception)
        {
            return new OperationError { Message = $"Could not read the game file: {exception.Message}", Exception = exception };
        }
    }

    public OperationResult<ExtractedCode> Extract(
        string imagePath,
        string destinationRoot,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            using var disc = OpenDisc(imagePath);
            var data = fileSystem.Path.Combine(destinationRoot, "DATA");

            WriteNew(fileSystem.Path.Combine(destinationRoot, "disc", "header.bin"), ReadExact(disc.Reader, 0, DiscHeaderSize));
            WriteNew(fileSystem.Path.Combine(destinationRoot, "disc", "region.bin"), ReadExact(disc.Reader, RegionOffset, RegionSize));
            WritePartitionHeader(disc, data);

            string mainDolSha;
            using (var partition = new PartitionReader(disc.Reader, disc.Game))
                mainDolSha = WriteSystemFiles(partition, fileSystem.Path.Combine(data, "sys"));

            using var files = DiscFileSystem.Open(disc.Reader, disc.Game);
            var all = EnumerateFiles(files.Root).ToList();
            var total = Math.Max(1, all.Sum(file => file.Size));
            var written = 0L;
            var relativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? staticRelSha = null;

            CreateDirectories(files.Root, fileSystem.Path.Combine(data, "files"));
            foreach (var file in all)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = ResolveTarget(data, file, relativePaths);
                var sha = CopyFile(files, file, target, hash: IsStaticRel(file), cancellationToken);
                if (sha is not null)
                    staticRelSha = sha;
                written += file.Size;
                progress?.Report((double)written / total);
            }

            if (staticRelSha is null)
                return Fail("This disc has no StaticR.rel, so it is not Mario Kart Wii.");

            progress?.Report(1);
            return new ExtractedCode(mainDolSha, staticRelSha);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new OperationError { Message = $"Extracting the game failed: {exception.Message}", Exception = exception };
        }
    }

    public OperationResult<ExtractedCode> RestoreCode(string imagePath, string dataPath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var disc = OpenDisc(imagePath);
            byte[] dol;
            using (var partition = new PartitionReader(disc.Reader, disc.Game))
                dol = ReadMainDol(partition);

            using var files = DiscFileSystem.Open(disc.Reader, disc.Game);
            var rel = files.Find(StaticRelDiscPath);
            if (rel is null || rel.IsDirectory)
                return Fail("This disc has no StaticR.rel, so it is not Mario Kart Wii.");

            var dolPath = fileSystem.Path.Combine(dataPath, "sys", "main.dol");
            var relPath = fileSystem.Path.Combine(dataPath, "files", "rel", "StaticR.rel");
            ReplaceFile(dolPath, temp => WriteNew(temp, dol));
            string? relSha = null;
            ReplaceFile(relPath, temp => relSha = CopyFile(files, rel, temp, hash: true, cancellationToken));
            return new ExtractedCode(Sha256(dol), relSha!);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new OperationError { Message = $"Restoring the game code failed: {exception.Message}", Exception = exception };
        }
    }

    #region Disc

    private sealed class OpenedDisc(IBlobReader reader, Partition game) : IDisposable
    {
        public IBlobReader Reader { get; } = reader;
        public Partition Game { get; } = game;

        public void Dispose() => Reader.Dispose();
    }

    private OpenedDisc OpenDisc(string imagePath)
    {
        // Read-only and shared: the image stays usable by anything else that has it open, and is never written.
        var stream = fileSystem.File.Open(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        IBlobReader reader;
        try
        {
            reader = Blob.Open(stream, imagePath, leaveOpen: false);
        }
        catch
        {
            // Blob.Open only takes ownership of the stream once it succeeds.
            stream.Dispose();
            throw;
        }

        try
        {
            if (!WiiVolume.IsWiiDisc(reader))
                throw new InvalidDataException("This file is not a Wii disc image.");
            var game = WiiVolume.GetPartitions(reader).Where(partition => partition.Type == GamePartitionType).ToList();
            if (game.Count == 0)
                throw new InvalidDataException("This disc has no game partition.");
            return new(reader, game[0]);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    private void WritePartitionHeader(OpenedDisc disc, string data)
    {
        // The partition header is not encrypted: the ticket, then the sizes and (shifted) offsets of the rest.
        var offset = (long)disc.Game.Offset;
        var header = ReadExact(disc.Reader, offset, 0x2C0);
        var tmdSize = (int)ReadUInt32(header, 0x2A4);
        var tmdOffset = (long)ReadUInt32(header, 0x2A8) << 2;
        var certSize = (int)ReadUInt32(header, 0x2AC);
        var certOffset = (long)ReadUInt32(header, 0x2B0) << 2;
        var h3Offset = (long)ReadUInt32(header, 0x2B4) << 2;

        WriteNew(fileSystem.Path.Combine(data, "ticket.bin"), header[..TicketSize]);
        WriteNew(fileSystem.Path.Combine(data, "tmd.bin"), ReadExact(disc.Reader, offset + tmdOffset, tmdSize));
        WriteNew(fileSystem.Path.Combine(data, "cert.bin"), ReadExact(disc.Reader, offset + certOffset, certSize));
        WriteNew(fileSystem.Path.Combine(data, "h3.bin"), ReadExact(disc.Reader, offset + h3Offset, H3Size));
    }

    private string WriteSystemFiles(PartitionReader partition, string sys)
    {
        var boot = ReadExact(partition, 0, BootSize);
        WriteNew(fileSystem.Path.Combine(sys, "boot.bin"), boot);
        WriteNew(fileSystem.Path.Combine(sys, "bi2.bin"), ReadExact(partition, BootSize, Bi2Size));

        var apploaderSize = WiiVolume.GetApploaderSize(partition) ?? throw new InvalidDataException("The disc has no apploader.");
        WriteNew(fileSystem.Path.Combine(sys, "apploader.img"), ReadExact(partition, ApploaderOffset, checked((int)apploaderSize)));

        var dol = ReadMainDol(partition);
        WriteNew(fileSystem.Path.Combine(sys, "main.dol"), dol);

        // Wii offsets in the boot header are stored shifted right by two.
        var fstOffset = (long)ReadUInt32(boot, 0x424) << 2;
        var fstSize = checked((int)((long)ReadUInt32(boot, 0x428) << 2));
        WriteNew(fileSystem.Path.Combine(sys, "fst.bin"), ReadExact(partition, fstOffset, fstSize));
        return Sha256(dol);
    }

    private static byte[] ReadMainDol(PartitionReader partition)
    {
        var offset = WiiVolume.GetBootDolOffset(partition) ?? throw new InvalidDataException("The disc has no main.dol.");
        var size = WiiVolume.GetBootDolSize(partition, offset) ?? throw new InvalidDataException("The disc's main.dol is unreadable.");
        return ReadExact(partition, checked((long)offset), checked((int)size));
    }

    private static byte[] ReadExact(IBlobReader reader, long offset, int length)
    {
        var buffer = new byte[length];
        var read = 0;
        while (read < length)
        {
            var count = reader.ReadAt(offset + read, buffer.AsSpan(read));
            if (count <= 0)
                throw new InvalidDataException("The game file ended early; it may be incomplete or damaged.");
            read += count;
        }

        return buffer;
    }

    private static uint ReadUInt32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    #endregion

    #region Files

    private static IEnumerable<DiscFileInfo> EnumerateFiles(DiscFileInfo directory)
    {
        foreach (var child in directory.Children)
        {
            if (!child.IsDirectory)
            {
                yield return child;
                continue;
            }

            foreach (var file in EnumerateFiles(child))
                yield return file;
        }
    }

    private static bool IsStaticRel(DiscFileInfo file) =>
        string.Equals(file.Path.TrimStart('/'), StaticRelDiscPath, StringComparison.OrdinalIgnoreCase);

    private void CreateDirectories(DiscFileInfo directory, string path)
    {
        fileSystem.Directory.CreateDirectory(path);
        foreach (var child in directory.Children.Where(child => child.IsDirectory))
            CreateDirectories(child, fileSystem.Path.Combine(path, SafeName(child.Name)));
    }

    /// <summary>
    /// Builds the output path from the disc's own names, which come from the image and are not trusted: a name that
    /// would climb out of the folder, or two names only a case-insensitive drive would merge, stop the extraction.
    /// </summary>
    private string ResolveTarget(string data, DiscFileInfo file, HashSet<string> seen)
    {
        var segments = new Stack<string>();
        for (var entry = file; entry is { IsRoot: false }; entry = entry.Parent)
            segments.Push(SafeName(entry.Name));

        var relative = string.Join('/', segments);
        if (!seen.Add(relative))
            throw new InvalidDataException($"The disc has two files named '{relative}' that only differ in capitalization.");
        return fileSystem.Path.Combine([data, "files", .. segments]);
    }

    private static readonly char[] UnsafeNameCharacters = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];

    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON",
        "PRN",
        "AUX",
        "NUL",
        "COM1",
        "COM2",
        "COM3",
        "COM4",
        "COM5",
        "COM6",
        "COM7",
        "COM8",
        "COM9",
        "LPT1",
        "LPT2",
        "LPT3",
        "LPT4",
        "LPT5",
        "LPT6",
        "LPT7",
        "LPT8",
        "LPT9",
    };

    /// <summary>The same rules on every platform, so an extraction that works on Linux also works on Windows.</summary>
    public static string SafeName(string name)
    {
        if (
            string.IsNullOrEmpty(name)
            || name is "." or ".."
            || name.IndexOfAny(UnsafeNameCharacters) >= 0
            || name.Any(char.IsControl)
            || name.EndsWith('.')
            || name.EndsWith(' ')
            || ReservedWindowsNames.Contains(name.Split('.')[0])
        )
            throw new InvalidDataException($"The disc contains a file name that can't be extracted safely: '{name}'.");
        return name;
    }

    /// <returns>The SHA-256 of the copied bytes when <paramref name="hash"/> is set.</returns>
    private string? CopyFile(DiscFileSystem files, DiscFileInfo file, string target, bool hash, CancellationToken cancellationToken)
    {
        using var output = fileSystem.FileStream.New(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var hasher = hash ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        Stream destination = hasher is null ? output : new HashingStream(output, hasher);
        var copied = files.CopyFileTo(file, destination, cancellationToken);
        output.Flush(flushToDisk: true);
        if (copied != file.Size || output.Length != file.Size)
            throw new InvalidDataException($"'{file.Path}' was not written completely.");
        return hasher is null ? null : Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
    }

    private void WriteNew(string path, byte[] contents)
    {
        fileSystem.Directory.CreateDirectory(fileSystem.Path.GetDirectoryName(path)!);
        using var output = fileSystem.FileStream.New(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        output.Write(contents);
        output.Flush(flushToDisk: true);
        if (output.Length != contents.Length)
            throw new InvalidDataException($"'{fileSystem.Path.GetFileName(path)}' was not written completely.");
    }

    /// <summary>Writes next to <paramref name="path"/> first, so a failed write leaves the current file as it was.</summary>
    private void ReplaceFile(string path, Action<string> write)
    {
        var temp = path + ".restore";
        if (fileSystem.File.Exists(temp))
            fileSystem.File.Delete(temp);
        try
        {
            write(temp);
            fileSystem.File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (fileSystem.File.Exists(temp))
                fileSystem.File.Delete(temp);
        }
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    private sealed class HashingStream(Stream inner, IncrementalHash hash) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            hash.AppendData(buffer);
            inner.Write(buffer);
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }

    #endregion
}
