using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Views;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Services.Launcher;

// IMPORTANT: This is just an example on the launcher
public class GoogleLauncher : ILauncher
{
    private bool installed = false;

    protected static GoogleLauncher? _instance;
    public static GoogleLauncher Instance => _instance ??= new();
    public string GameTitle => "Google";

    public Task<OperationResult> Launch()
    {
        ViewUtils.OpenLink("https://www.google.com/");
        return Task.FromResult(Ok());
    }

    public async Task<OperationResult> Install()
    {
        installed = true;
        await new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Message)
            .SetTitleText("Installed google")
            .SetInfoText("just kidding, this is just a test launch option. we didn't do anything")
            .ShowDialog();
        return Ok();
    }

    public Task<OperationResult> Update() => Task.FromResult(Ok());

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        var serverEnabled = await HttpClientHelper.GetAsync<string>("https://www.google.com/");
        if (!serverEnabled.Succeeded)
            return WheelWizardStatus.NoServer;

        return !installed ? WheelWizardStatus.NotInstalled : WheelWizardStatus.Ready;
    }
}
