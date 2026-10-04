using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using WheelWizard.ApplicationData;
using WheelWizard.ApplicationLifecycle.Logging;

namespace WheelWizard.Test.Features.ApplicationLifecycle;

public class ApplicationLogFilesTests
{
    [Fact]
    public void ExistingInjectedLogger_FollowsRelocationAfterFilesAreReleased()
    {
        var directory = "before";
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(_ => directory);
        var sinks = new Dictionary<string, RecordingSink>();
        var filesFactory = Substitute.For<ILogFileFactory>();
        filesFactory
            .Create(Arg.Any<string>())
            .Returns(call =>
            {
                var sink = new RecordingSink();
                sinks.Add(call.Arg<string>(), sink);
                return new LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();
            });
        using var files = new ApplicationLogFiles(location, filesFactory);
        files.Start();
        using var root = new LoggerConfiguration().WriteTo.Sink(files).CreateLogger();
        using var factory = LoggerFactory.Create(builder => builder.AddSerilog(root, dispose: false));
        var logger = factory.CreateLogger("ExistingService");
        logger.LogInformation("Before move");

        using (files.Pause())
        {
            Assert.True(sinks["before"].Disposed);
            directory = "after";
            logger.LogInformation("While files are paused");
        }
        logger.LogInformation("After move");

        Assert.Single(sinks["before"].Events);
        var written = Assert.Single(sinks["after"].Events);
        Assert.Equal("After move", written.RenderMessage());
        Assert.Equal("ExistingService", ((ScalarValue)written.Properties["SourceContext"]).Value);
    }

    [Fact]
    public void NestedPauses_ResumeOnlyOnce_AndCannotReopenAfterDisposal()
    {
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns("data");
        var factory = Substitute.For<ILogFileFactory>();
        factory.Create("data").Returns(_ => new LoggerConfiguration().CreateLogger());
        var files = new ApplicationLogFiles(location, factory);
        files.Start();
        var first = files.Pause();
        var second = files.Pause();
        first.Dispose();
        first.Dispose();
        factory.Received(1).Create("data");
        second.Dispose();
        factory.Received(2).Create("data");
        var last = files.Pause();
        files.Dispose();
        last.Dispose();
        factory.Received(2).Create("data");
    }

    [Fact]
    public void FailedFileReopen_DoesNotBreakTheOtherLogOutputs()
    {
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns("data");
        var factory = Substitute.For<ILogFileFactory>();
        factory.Create("data").Returns(_ => new LoggerConfiguration().CreateLogger(), _ => throw new IOException("Unavailable"));
        using var files = new ApplicationLogFiles(location, factory);
        files.Start();
        var output = new RecordingSink();
        using var root = new LoggerConfiguration().WriteTo.Sink(files).WriteTo.Sink(output).CreateLogger();
        using (files.Pause()) { }
        root.Information("Still logging");
        Assert.Equal("Still logging", Assert.Single(output.Events).RenderMessage());
    }

    [Fact]
    public void NativeFiles_CanMoveAndResumeThroughTheSameLogger()
    {
        var rootDirectory = Path.Combine(Path.GetTempPath(), "wheelwizard-log-test-" + Guid.NewGuid().ToString("N"));
        var directory = Path.Combine(rootDirectory, "before");
        var destination = Path.Combine(rootDirectory, "after");
        var location = Substitute.For<IApplicationDataLocation>();
        location.DirectoryPath.Returns(_ => directory);
        try
        {
            using (var files = new ApplicationLogFiles(location, new LogFileFactory(new Testably.Abstractions.RealFileSystem())))
            using (var logger = new LoggerConfiguration().WriteTo.Sink(files).CreateLogger())
            {
                files.Start();
                logger.Information("Before relocation");
                using (files.Pause())
                {
                    Directory.Move(directory, destination);
                    directory = destination;
                }
                logger.Information("After relocation");
            }
            var log = Assert.Single(Directory.GetFiles(Path.Combine(destination, "logs"), "*.txt"));
            var text = File.ReadAllText(log);
            Assert.Contains("Before relocation", text);
            Assert.Contains("After relocation", text);
        }
        finally
        {
            if (Directory.Exists(rootDirectory))
                Directory.Delete(rootDirectory, recursive: true);
        }
    }

    private sealed class RecordingSink : ILogEventSink, IDisposable
    {
        public List<LogEvent> Events { get; } = [];
        public bool Disposed { get; private set; }

        public void Emit(LogEvent logEvent) => Events.Add(logEvent);

        public void Dispose() => Disposed = true;
    }
}
