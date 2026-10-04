using System.IO.Abstractions;
using Serilog;
using Serilog.Core;
using Serilog.Debugging;
using Serilog.Events;
using WheelWizard.ApplicationData;

namespace WheelWizard.ApplicationLifecycle.Logging;

public interface IApplicationLogFiles
{
    IDisposable Pause();
}

public interface ILogFileFactory
{
    Logger Create(string applicationDataDirectory);
}

public sealed class LogFileFactory(IFileSystem fileSystem) : ILogFileFactory
{
    public Logger Create(string applicationDataDirectory)
    {
        var logs = fileSystem.Path.Combine(applicationDataDirectory, "logs");
        fileSystem.Directory.CreateDirectory(logs);
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(fileSystem.Path.Combine(logs, "log.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();
    }
}

/// <summary>Keeps the logger pipeline stable while releasing and reopening its movable file output.</summary>
public sealed class ApplicationLogFiles(IApplicationDataLocation location, ILogFileFactory factory)
    : IApplicationLogFiles,
        ILogEventSink,
        IDisposable
{
    private readonly object _sync = new();
    private Logger? _fileLogger;
    private int _pauseCount;
    private bool _started;
    private bool _disposed;

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _started = true;
            if (_pauseCount == 0 && _fileLogger is null)
                _fileLogger = factory.Create(location.DirectoryPath);
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_sync)
            _fileLogger?.Write(logEvent);
    }

    public IDisposable Pause()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            ++_pauseCount;
            _fileLogger?.Dispose();
            _fileLogger = null;
            return new PauseScope(this);
        }
    }

    private void Resume()
    {
        lock (_sync)
        {
            if (--_pauseCount != 0 || _disposed || !_started)
                return;
            try
            {
                _fileLogger = factory.Create(location.DirectoryPath);
            }
            catch (Exception exception)
            {
                // Keep console logging alive and do not replace a data-move result with a logging failure.
                SelfLog.WriteLine("Could not reopen the application log file: {0}", exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _fileLogger?.Dispose();
            _fileLogger = null;
        }
    }

    private sealed class PauseScope(ApplicationLogFiles owner) : IDisposable
    {
        private ApplicationLogFiles? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Resume();
    }
}
