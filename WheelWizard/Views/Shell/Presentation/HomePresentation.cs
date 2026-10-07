using WheelWizard.Settings.Views;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Views.Dialogs;
using WheelWizard.Views.Shell;
using WheelWizard.Views.Shell.Navigation;
using WheelWizard.Views.Shell.ViewModels;

namespace WheelWizard.Views.Shell.Presentation;

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
