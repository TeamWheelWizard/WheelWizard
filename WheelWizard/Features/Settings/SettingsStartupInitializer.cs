using Microsoft.Extensions.Logging;

namespace WheelWizard.Settings;

public sealed class SettingsStartupInitializer(
    ISettingsManager settingsManager,
    ISettingsSignalBus settingsSignalBus,
    ISettingsLocalizationService localizationService,
    ILogger<SettingsStartupInitializer> logger
) : ISettingsStartupInitializer
{
    public void Initialize()
    {
        SettingsSignalRuntime.Initialize(settingsSignalBus);
        SettingsRuntime.Initialize(settingsManager);
        settingsManager.LoadSettings();
        localizationService.Initialize();

        var reportResult = settingsManager.ValidateCorePathSettings();
        if (reportResult.IsFailure)
        {
            logger.LogError(reportResult.Error.Exception, "Failed to validate startup settings: {Message}", reportResult.Error.Message);
            return;
        }

        var report = reportResult.Value;
        if (report.IsValid)
            return;

        foreach (var issue in report.Issues)
        {
            logger.LogWarning(
                "Settings validation warning: {Code} ({SettingName}) {Message}",
                issue.Code,
                issue.SettingName,
                issue.Message
            );
        }
    }
}
