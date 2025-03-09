using Avalonia.Interactivity;
using Avalonia.Threading;
using WheelWizard.Helpers;
using WheelWizard.Services.LiveData;
using WheelWizard.Services.WiiManagement;
using WheelWizard.Utilities;
using WheelWizard.Utilities.RepeatedTasks;
using WheelWizard.Views.Popups.Generic;

namespace WheelWizard.Views.Popups;

public partial class DevToolWindow : PopupContent, IRepeatedTaskListener
{
    public DevToolWindow() : base(true, true, true, "Dev Tool", size: new(440, 600))
    {
        InitializeComponent();
        AppStateMonitor.Instance.Subscribe(this);
        LoadSettings();
    }
    
    protected override void BeforeClose()
    {
        AppStateMonitor.Instance.Unsubscribe(this);
        base.BeforeClose();
    }
    
    // Yes, it would absolutely be more optimized to insteadof every x seconds refreshing, to just refresh when something changes
    // However, We explicitly do it this way, so all code in the codebase can stay unchanged. The idea is that you can remove the AppStateMonitor and everything will still work
    // This is indeed also possible if you make it if you make everything an observer pattern, where-ever you want to monitor something.
    // However, the problem with that is that we will everything an observer pattern, but something like the MiiImageManager has no reason
    // to be an observer pattern besides this, and it would make the codebase more complex for no reason.
    public void OnUpdate(RepeatedTaskManager sender)
    {
        RrRefreshTimeLeft.Text = RRLiveRooms.Instance.TimeUntilNextTick.Seconds.ToString();
        MiiImagesCashed.Text = MiiImageManager.ImageCount.ToString();
        MiiParsedDataCashed.Text = MiiImageManager.ParsedMiiDataCount.ToString();
    }
    
    private void LoadSettings()
    {
        WhWzTopMost.IsChecked = ViewUtils.GetLayout().Topmost;
        HttpHelperOff.IsChecked = !HttpClientHelper.FakeConnectionToInternet;
    }
    
    private void WhWzTopMost_OnClick(object sender, RoutedEventArgs e) => ViewUtils.GetLayout().Topmost = WhWzTopMost.IsChecked == true;
    private void HttpHelperOff_OnClick(object sender, RoutedEventArgs e) => HttpClientHelper.FakeConnectionToInternet = HttpHelperOff.IsChecked != true;
    private void ForceEnableLayout_OnClick(object sender, RoutedEventArgs e) => ViewUtils.GetLayout().SetInteractable(true);

    private void ClearImageCache_OnClick(object sender, RoutedEventArgs e) => MiiImageManager.ClearImageCache();
    
    
    #region Popup Tests
    private async void TestProgressPopup_OnClick(object sender, RoutedEventArgs e)
    {
        ProgressButtonTest.IsEnabled = false;
        var progressWindow = new ProgressWindow("test progress !!");
        progressWindow.SetGoal("Setting a goal!");
        progressWindow.Show();
        
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                progressWindow.UpdateProgress(i * 20);
                progressWindow.SetExtraText($"This is information for iteration {i}");
                if(i == 3) progressWindow.SetGoal($"Changed the Goal");
            });
            await Task.Delay(1000); 
        }
        Dispatcher.UIThread.Invoke(() =>
        {
            progressWindow.Close();
            ProgressButtonTest.IsEnabled = true;
        });
    }
    
    private void TestMessagePopups_OnClick(object sender, RoutedEventArgs e)
    {
       new MessageBoxWindow()
            // .SetMessageType(MessageBoxWindow.MessageType.Message) // Default, so you dont have to type this
            .SetTitleText("Saved Successfully!")
            .SetInfoText("The name you entered has sucessfully saved in the system")
            .Show();
       
       new MessageBoxWindow()
           .SetMessageType(MessageBoxWindow.MessageType.Warning)
           .SetTitleText("Invalid license.")
           .SetInfoText("This license has no Mii data or is incomplete.\n" +
                        "Please use the Mii Channel to create a Mii first. \n \n \n abncd")
           .Show();
       
       new MessageBoxWindow()
           .SetMessageType(MessageBoxWindow.MessageType.Error)
           .SetTitleText("Update error.")
           .SetInfoText("An error occured when trying top update Retro Rewind.")
           .Show();
    }

    #endregion
}
