using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Navigation;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Pages;

public sealed class HomePresentation(INavigationService navigation) : IHomePresentation
{
    public void ShowError(OperationError error) => MessageTranslationHelper.ShowMessage(error);

    public void OpenSettings() => navigation.NavigateTo<SettingsPage>();

    public void SetApplicationInteractable(bool interactable) => ViewUtils.GetLayout().SetInteractable(interactable);

    public async Task ShowLaunchPromptAsync(HomeLaunchPrompt prompt) =>
        await new YesNoWindow()
            .SetMainText(prompt.MainText)
            .SetExtraText(prompt.ExtraText)
            .SetButtonText(prompt.YesText, prompt.NoText)
            .AwaitAnswer();
}
