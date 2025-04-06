using Microsoft.Extensions.Logging;

namespace WheelWizard;

public class CustomLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return Log.GetLoggerFactory().CreateLogger(categoryName);
    }

    public void Dispose()
    {
        // Do nothing
        GC.SuppressFinalize(this);
    }
}
