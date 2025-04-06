using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using WheelWizard.Services;

namespace WheelWizard;

public static class Logging
{
    /// <summary>
    /// Creates a static logger instance for the application.
    /// </summary>
    /// <remarks>
    /// Do not call this method multiple times. It is intended to be called once at application startup.
    /// Do not use the static logger instance other than for the application startup.
    /// </remarks>
    public static void CreateStaticLogger()
    {
        try
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Console(theme: AnsiConsoleTheme.Sixteen, applyThemeToRedirectedOutput: true)
                .WriteTo.File(Path.Combine(PathManager.WheelWizardAppdataPath, "logs/log.txt"), rollingInterval: RollingInterval.Day)
                .CreateLogger();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        // Log the application start
        LogPlatformInformation();
    }

    /// <summary>
    /// Logs the platform information at application startup.
    /// </summary>
    private static void LogPlatformInformation()
    {
        // ReSharper disable once RedundantAssignment
        var modeCheck = "release";

        // ReSharper disable once ConvertToConstant.Local
        var osCheck = "unknown";

#if DEBUG
        modeCheck = "debug";
#endif

#if WINDOWS
        osCheck = "windows";
#elif LINUX
        osCheck = "linux";
#elif MACOS
        osCheck = "macos";
#endif

        Log.Information("Application start [Configuration: {Configuration}, OS: {OS}]", modeCheck, osCheck);
    }
}
