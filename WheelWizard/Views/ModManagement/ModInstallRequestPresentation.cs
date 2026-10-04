using WheelWizard.GameBanana.InstallRequests;
using WheelWizard.Views.Popups;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.Views.Popups.ModManagement;

namespace WheelWizard.Views.ModManagement;

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
