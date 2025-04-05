using Microsoft.Extensions.Logging;

namespace WheelWizard;

public static class Log
{

    private static ServiceProvider s_loggingServiceProvider = new ServiceCollection().BuildServiceProvider();

    public static void RegisterLoggingServiceProvider(ServiceProvider serviceProvider)
    {
        s_loggingServiceProvider = serviceProvider;
    }

    public static ILogger GetLogger<T>()
    {
        return s_loggingServiceProvider.GetRequiredService<ILogger<T>>();
    }

}
