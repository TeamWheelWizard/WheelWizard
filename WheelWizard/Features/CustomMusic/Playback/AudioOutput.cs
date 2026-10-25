using System.Diagnostics;
using System.IO.Abstractions;
using System.Runtime.InteropServices;

namespace WheelWizard.CustomMusic.Playback;

/// <summary>Plays one WAV file at a time through the operating system. No third-party audio library is needed.</summary>
public interface IAudioOutput : IDisposable
{
    /// <summary>False when this computer has no way to play audio (for example a Linux system without any player installed).</summary>
    bool IsAvailable { get; }

    /// <summary>Starts playing <paramref name="wav"/> immediately, replacing whatever was playing.</summary>
    void Start(byte[] wav);

    void Stop();
}

public static class AudioOutput
{
    public static IAudioOutput Create(IFileSystem fileSystem) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsAudioOutput() : new ProcessAudioOutput(fileSystem);

    /// <summary>Finds an executable on PATH (and the usual extra folders on macOS).</summary>
    internal static string? FindExecutable(IFileSystem fileSystem, string name)
    {
        var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? name + ".exe" : name;
        var candidates = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(fileSystem.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(directory => fileSystem.Path.Combine(directory.Trim('"'), fileName))
            .ToList();
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            candidates.AddRange(["/usr/bin/" + fileName, "/opt/homebrew/bin/" + fileName, "/usr/local/bin/" + fileName]);
        return candidates.FirstOrDefault(fileSystem.File.Exists);
    }
}

/// <summary>Windows: the multimedia PlaySound API plays a WAV straight from memory.</summary>
internal sealed class WindowsAudioOutput : IAudioOutput
{
    private const uint SndAsync = 0x0001;
    private const uint SndNoDefault = 0x0002;
    private const uint SndMemory = 0x0004;

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PlaySound(IntPtr sound, IntPtr module, uint flags);

    private readonly object _gate = new();

    // Windows keeps reading the buffer while the sound plays, so it stays allocated until playback stops.
    private IntPtr _buffer;

    public bool IsAvailable => true;

    public void Start(byte[] wav)
    {
        lock (_gate)
        {
            var next = Marshal.AllocHGlobal(wav.Length);
            try
            {
                Marshal.Copy(wav, 0, next, wav.Length);
                // Starting a new sound stops the previous one first, after which its buffer can go.
                if (!PlaySound(next, IntPtr.Zero, SndMemory | SndAsync | SndNoDefault))
                    throw new IOException("Windows could not play the audio.");
            }
            catch
            {
                Marshal.FreeHGlobal(next);
                throw;
            }

            Release();
            _buffer = next;
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            PlaySound(IntPtr.Zero, IntPtr.Zero, 0);
            Release();
        }
    }

    private void Release()
    {
        if (_buffer == IntPtr.Zero)
            return;
        Marshal.FreeHGlobal(_buffer);
        _buffer = IntPtr.Zero;
    }

    public void Dispose() => Stop();
}

/// <summary>macOS and Linux: hands a temporary WAV file to the system's command line player.</summary>
internal sealed class ProcessAudioOutput(IFileSystem fileSystem) : IAudioOutput
{
    private static readonly (string Name, string[] Arguments)[] Players =
    [
        ("afplay", []),
        ("paplay", []),
        ("pw-play", []),
        ("aplay", ["-q"]),
        ("ffplay", ["-nodisp", "-autoexit", "-loglevel", "quiet"]),
        ("play", ["-q"]),
        ("mpv", ["--no-video", "--really-quiet"]),
    ];

    private readonly object _gate = new();
    private readonly Lazy<(string Path, string[] Arguments)?> _player = new(() => FindPlayer(fileSystem));
    private Process? _process;
    private string? _file;

    public bool IsAvailable => _player.Value != null;

    public void Start(byte[] wav)
    {
        var player = _player.Value ?? throw new NotSupportedException("No audio player was found. Install PulseAudio, PipeWire, ALSA or FFmpeg to preview music.");
        lock (_gate)
        {
            Stop();
            var file = fileSystem.Path.Combine(fileSystem.Path.GetTempPath(), $"wheelwizard-preview-{Guid.NewGuid():N}.wav");
            fileSystem.File.WriteAllBytes(file, wav);

            var startInfo = new ProcessStartInfo
            {
                FileName = player.Path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var argument in player.Arguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.ArgumentList.Add(file);

            try
            {
                var process = Process.Start(startInfo) ?? throw new IOException("The audio player could not be started.");
                process.EnableRaisingEvents = true;
                process.Exited += (_, _) => DeleteQuietly(file);
                // Nobody reads the player's output; draining it keeps it from blocking.
                process.OutputDataReceived += (_, _) => { };
                process.ErrorDataReceived += (_, _) => { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _process = process;
                _file = file;
            }
            catch
            {
                DeleteQuietly(file);
                throw;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            var process = _process;
            var file = _file;
            _process = null;
            _file = null;
            try
            {
                if (process is { HasExited: false })
                    process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already finished.
            }

            process?.Dispose();
            if (file != null)
                DeleteQuietly(file);
        }
    }

    private static (string Path, string[] Arguments)? FindPlayer(IFileSystem fileSystem)
    {
        foreach (var (name, arguments) in Players)
        {
            if (AudioOutput.FindExecutable(fileSystem, name) is { } path)
                return (path, arguments);
        }

        return null;
    }

    private void DeleteQuietly(string file)
    {
        try
        {
            fileSystem.File.Delete(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp file left behind is harmless.
        }
    }

    public void Dispose() => Stop();
}
