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
    public static GoogleLauncher Instance => _instance ??= new GoogleLauncher();
    public string GameTitle => "Google";
    public Task Launch()
    {
        ViewUtils.OpenLink("https://www.google.com/");
        return Task.CompletedTask;
    }

    public Task Install()
    {
        installed = true;
        return new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Message)
            .SetTitleText("Installed google")
            .SetInfoText("just kidding, this is just a test launch option. we didn't do anything")
            .ShowDialog();
    }
    public Task Update() => Task.CompletedTask;

    public async Task<WheelWizardStatus> GetCurrentStatus()
    {
        var serverEnabled = await HttpClientHelper.GetAsync<string>("https://www.google.com/");
        if(!serverEnabled.Succeeded) return WheelWizardStatus.NoServer;

        return !installed ? WheelWizardStatus.NotInstalled : WheelWizardStatus.Ready;
    }
}
