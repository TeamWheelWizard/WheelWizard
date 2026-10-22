using System.Diagnostics;
using System.IO.Abstractions;
using System.Runtime.InteropServices;

namespace WheelWizard.CustomMusic.Audio.Formats;

/// <summary>
/// Optional fallback for formats without a managed decoder (M4A/AAC, Opus, WMA, ALAC, ...).
/// It only works when the user has FFmpeg installed; Wheel Wizard never downloads it.
/// </summary>
public static class FfmpegReader
{
    private const int OutputSampleRate = 48000;

    /// <summary>FFmpeg next to Wheel Wizard, on PATH, or (on macOS) in Homebrew's folders.</summary>
    public static string? FindExecutable(IFileSystem fileSystem)
    {
        var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new List<string> { fileSystem.Path.Combine(AppContext.BaseDirectory, name) };

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        candidates.AddRange(
            pathVariable
                .Split(fileSystem.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Select(directory => fileSystem.Path.Combine(directory.Trim('"'), name))
        );

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            candidates.AddRange(["/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg"]);

        return candidates.FirstOrDefault(fileSystem.File.Exists);
    }

    public static bool IsAvailable(IFileSystem fileSystem) => FindExecutable(fileSystem) != null;

    public static async Task<AudioBuffer> ReadAsync(IFileSystem fileSystem, string path, CancellationToken cancellationToken = default)
    {
        var executable =
            FindExecutable(fileSystem) ?? throw new NotSupportedException("This audio format needs FFmpeg. Install FFmpeg, or convert the song to MP3, FLAC, OGG or WAV.");

        string[] arguments = ["-v", "error", "-nostdin", "-i", path, "-vn", "-ac", "2", "-ar", OutputSampleRate.ToString(), "-f", "f32le", "-acodec", "pcm_f32le", "pipe:1"];
        var (exitCode, output, errors) = await RunAsync(executable, arguments, cancellationToken);
        if (exitCode != 0 || output.Count < 8)
        {
            // FFmpeg's own error output is long and technical; keep only its last line for the log/tooltip.
            var detail = errors.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            throw new InvalidDataException(detail == null ? "FFmpeg could not decode this file." : $"FFmpeg could not decode this file ({detail}).");
        }

        var frames = output.Count / 8;
        var left = new float[frames];
        var right = new float[frames];
        var samples = MemoryMarshal.Cast<byte, float>(output.AsSpan(0, frames * 8));
        for (var i = 0; i < frames; i++)
        {
            left[i] = samples[i * 2];
            right[i] = samples[i * 2 + 1];
        }

        return new AudioBuffer([left, right], OutputSampleRate);
    }

    /// <summary>
    /// Title, artist, album and embedded cover art of a file only FFmpeg understands (Opus, WebM, WMA, ...).
    /// Never throws: missing FFmpeg or unreadable tags just give empty metadata.
    /// </summary>
    public static async Task<AudioMetadata> ReadMetadataAsync(IFileSystem fileSystem, string path, CancellationToken cancellationToken = default)
    {
        var metadata = new AudioMetadata();
        if (FindExecutable(fileSystem) is not { } executable)
            return metadata;

        try
        {
            // Container tags first (Matroska/WebM, MP4, ...); Ogg keeps its tags on the audio stream instead.
            string[][] sources =
            [
                ["-v", "error", "-nostdin", "-i", path, "-f", "ffmetadata", "pipe:1"],
                ["-v", "error", "-nostdin", "-i", path, "-map_metadata", "0:s:a:0", "-f", "ffmetadata", "pipe:1"],
            ];
            foreach (var arguments in sources)
            {
                if (metadata.Title != null && metadata.Artist != null)
                    break;
                var (exitCode, output, _) = await RunAsync(executable, arguments, cancellationToken);
                if (exitCode == 0)
                    ApplyFfmetadata(System.Text.Encoding.UTF8.GetString(output.AsSpan()), metadata);
            }

            // An attached picture (Opus/FLAC pictures, MP4 covr, ...) shows up as a video stream; copy its first frame.
            string[] coverArguments = ["-v", "error", "-nostdin", "-i", path, "-map", "0:v:0?", "-frames:v", "1", "-c:v", "copy", "-f", "image2pipe", "pipe:1"];
            var (coverExit, cover, _) = await RunAsync(executable, coverArguments, cancellationToken);
            if (coverExit == 0 && cover.Count is > 0 and < 20 * 1024 * 1024)
                metadata.Cover = CoverArt.FromBytes(cover.ToArray());
        }
        catch (Exception exception) when (exception is InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            // Tags are a nice-to-have; the song still converts without them.
        }

        return metadata;
    }

    /// <summary>
    /// Reads FFmpeg's "ffmetadata" text, filling only what <paramref name="metadata"/> does not have yet.
    /// Keys before any [STREAM] section win. Values escape '=', ';', '#', '\' and line breaks with a backslash.
    /// </summary>
    public static void ApplyFfmetadata(string text, AudioMetadata metadata)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || line[0] is ';' or '#' or '[')
                continue;

            // A trailing (unescaped) backslash continues the value on the next line.
            builder.Clear();
            builder.Append(line);
            while (EndsWithEscape(builder) && index + 1 < lines.Length)
            {
                builder.Length--;
                builder.Append('\n').Append(lines[++index]);
            }

            var entry = builder.ToString();
            var equals = IndexOfUnescaped(entry, '=');
            if (equals <= 0)
                continue;
            var key = Unescape(entry[..equals]).Trim();
            var value = Unescape(entry[(equals + 1)..]).Trim();
            if (value.Length > 0)
                values.TryAdd(key, value); // the first (global) occurrence wins
        }

        metadata.Title ??= values.GetValueOrDefault("title");
        metadata.Artist ??= values.GetValueOrDefault("artist") ?? values.GetValueOrDefault("album_artist");
        metadata.Album ??= values.GetValueOrDefault("album");

        static bool EndsWithEscape(System.Text.StringBuilder value)
        {
            var slashes = 0;
            for (var i = value.Length - 1; i >= 0 && value[i] == '\\'; i--)
                slashes++;
            return slashes % 2 == 1;
        }
    }

    private static int IndexOfUnescaped(string text, char wanted)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\')
                i++;
            else if (text[i] == wanted)
                return i;
        }

        return -1;
    }

    private static string Unescape(string text)
    {
        if (!text.Contains('\\'))
            return text;
        var builder = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
                i++;
            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    /// <returns>The exit code, everything written to standard output (without copying the buffer), and standard error.</returns>
    private static async Task<(int ExitCode, ArraySegment<byte> Output, string Errors)> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken cancellationToken
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("FFmpeg could not be started.");
        await using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited.
            }
        });

        using var output = new MemoryStream();
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.StandardOutput.BaseStream.CopyToAsync(output, cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var errors = await errorTask;
        return (process.ExitCode, new ArraySegment<byte>(output.GetBuffer(), 0, (int)output.Length), errors);
    }
}
