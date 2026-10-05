using Serilog;
using Serilog.Core;
using Serilog.Sinks.SystemConsole.Themes;

namespace WheelWizard.ApplicationLifecycle.Logging;

public static class ApplicationLogging
{
    public static Logger CreateLogger(ApplicationLogFiles files)
    {
        files.Start();
        return new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
            .WriteTo.Sink(files)
            .CreateLogger();
    }

    public static void LogStartup(ILogger logger)
    {
        var configuration = Shared.DevelopmentMode.IsEnabled ? "debug" : "release";
        var platform =
            OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsMacOS() ? "macos"
            : "unknown";
        logger.Information("Application start [Configuration: {Configuration}, OS: {OS}]", configuration, platform);
    }
}
