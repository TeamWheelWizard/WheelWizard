using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.MessageTranslations;

namespace WheelWizard.Views.Pages.Settings;

internal static class SettingsEditing
{
    public static bool Set<T>(ISettingsManager settings, Setting<T> setting, T value)
    {
        if (settings.Set(setting, value))
            return true;
        if (setting.SaveError is { } error)
            MessageTranslationHelper.ShowMessage(Fail(error));
        else
            MessageTranslationHelper.ShowMessage(MessageTranslation.Warning_InvalidPathSettings);
        return false;
    }
}
