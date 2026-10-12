using WheelWizard.GameBanana.InstallRequests;
using WheelWizard.Mods.Views.Dialogs;
using WheelWizard.Views.Dialogs;

namespace WheelWizard.Mods.Presentation;

public sealed class ModInstallRequestPresentation(IPopupFactory popups) : IModInstallRequestPresentation
{
    public async Task ShowModAsync(ModInstallRequest request)
    {
        var popup = popups.Create<ModIndependentWindow>();
        await popup.LoadModAsync(request.ModId, request.DownloadUrl);
        await popup.ShowDialog();
    }

    public void ShowError(string reason) =>
        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Error)
            .SetTitleText("Couldn't load URL")
            .SetInfoText($"Error handling URL: {reason}")
            .Show();
}
