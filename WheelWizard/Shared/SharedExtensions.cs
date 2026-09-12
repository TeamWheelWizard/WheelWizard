using System.IO.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using Testably.Abstractions;
using WheelWizard.Shared.Calendar;
using WheelWizard.Shared.Downloads;
using WheelWizard.Shared.IO;
using WheelWizard.Shared.Processes;
using WheelWizard.Shared.Services;

namespace WheelWizard.Shared;

public static class SharedExtensions
{
    public static IServiceCollection AddSharedServices(this IServiceCollection services)
    {
        services.AddSingleton<IUnixCommandService, UnixCommandService>();
        services.AddSingleton<IUnixProcessService, UnixProcessService>();
        services.AddSingleton<IProcessLauncher, ProcessLauncher>();
        services.AddDownloads();
        services.AddSingleton<IApplicationProcess, ApplicationProcess>();
        services.AddSingleton<ISeasonalCalendar, SeasonalCalendar>();
        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton<IDirectoryTransferService, DirectoryTransferService>();
        services.AddSingleton<ITimeSystem, RealTimeSystem>();
        services.AddSingleton<IRandomSystem, RealRandomSystem>();
        services.AddSingleton<IMemoryCache>(_ => new MemoryCache(new MemoryCacheOptions()));
        services.AddLogging(builder => builder.AddSerilog(Log.Logger, dispose: false));
        services.AddTransient(typeof(IApiCaller<>), typeof(ApiCaller<>));
        return services;
    }
}
