using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using WheelWizard.Services;

namespace WheelWizard;

public static class Log
{

    private static ILoggerFactory s_loggerFactory = new LoggerFactory();

    public static void Initialize()
    {
        var logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
                .WriteTo.File(Path.Combine(PathManager.WheelWizardAppdataPath, "logs/log.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
        s_loggerFactory = new LoggerFactory().AddSerilog(logger, dispose: true);
    }

    public static void Dispose()
    {
        s_loggerFactory.Dispose();
    }
    public static ILoggerFactory GetLoggerFactory()
    {
        return s_loggerFactory;
    }

    public static ILogger<T> GetLogger<T>()
    {
        return s_loggerFactory.CreateLogger<T>();
    }

}
