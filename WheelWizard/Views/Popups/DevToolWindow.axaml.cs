using Avalonia.Interactivity;
using Avalonia.Threading;
using Microsoft.Extensions.Caching.Memory;
using WheelWizard.Launching;
using WheelWizard.RrRooms;
using WheelWizard.Shared;
using WheelWizard.Shared.MessageTranslations;
using WheelWizard.Shared.Polling;
using WheelWizard.Views.Components;
using WheelWizard.Views.Diagnostics;
using WheelWizard.Views.Popups.Base;
using WheelWizard.Views.Popups.Generic;
using WheelWizard.WheelWizardData;

namespace WheelWizard.Views.Popups;

public partial class DevToolWindow : PopupContent, IPollingListener
{
    private IMemoryCache Cache { get; }

    private IDolphinLaunchService DolphinLaunchService { get; }

    private LiveRoomsService LiveRooms { get; }

    private DevelopmentRefreshService DevelopmentRefresh { get; }

    public DevToolWindow(
        IMemoryCache cache,
        IDolphinLaunchService dolphinLaunchService,
        LiveRoomsService liveRooms,
        DevelopmentRefreshService developmentRefresh
    )
        : base(true, true, true, "Dev Tool")
    {
        Cache = cache;
        DolphinLaunchService = dolphinLaunchService;
        LiveRooms = liveRooms;
        DevelopmentRefresh = developmentRefresh;
        InitializeComponent();
        DevelopmentRefresh.Subscribe(this);
        LoadSettings();
    }

    protected override void BeforeClose()
    {
        DevelopmentRefresh.Unsubscribe(this);
        base.BeforeClose();
    }

    public void OnUpdate(ObservablePollingService sender)
    {
        RrRefreshTimeLeft.Text = LiveRooms.TimeUntilNextTick.Seconds.ToString();
        MiiImagesCashed.Text = ((MemoryCache)Cache).Count.ToString();
    }

    private void LoadSettings()
    {
        WhWzTopMost.IsChecked = ViewUtils.GetLayout().Topmost;
    }

    private void WhWzTopMost_OnClick(object sender, RoutedEventArgs e) => ViewUtils.GetLayout().Topmost = WhWzTopMost.IsChecked == true;

    private void ForceEnableLayout_OnClick(object sender, RoutedEventArgs e) => ViewUtils.GetLayout().SetInteractable(true);

    private void ClearCache_OnClick(object sender, RoutedEventArgs e) => ((MemoryCache)Cache).Clear();

    private void HideDevelopmentFeatures_OnClick(object sender, RoutedEventArgs e)
    {
        DevelopmentMode.Hide();
        ViewUtils.GetLayout().HideDevelopmentFeatures();
        Close();
    }

    private async void MiiChannel_OnClick(object? sender, RoutedEventArgs e) =>
        await DolphinLaunchService.LaunchDolphin(" -b -n 0001000248414341");

    #region Popup Tests

    private async void TestProgressPopup_OnClick(object sender, RoutedEventArgs e)
    {
        ProgressPopupTest.IsEnabled = false;
        var progressWindow = new ProgressWindow("test progress !!");
        progressWindow.SetGoal("Setting a goal!");
        progressWindow.Show();

        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.Invoke(() =>
            {
                progressWindow.UpdateProgress(i * 20);
                progressWindow.SetExtraText($"This is information for iteration {i}");
                if (i == 3)
                    progressWindow.SetGoal($"Changed the Goal");
            });
            await Task.Delay(1000);
        }

        Dispatcher.UIThread.Invoke(() =>
        {
            progressWindow.Close();
            ProgressPopupTest.IsEnabled = true;
        });
    }

    private void TestMessagePopups_OnClick(object sender, RoutedEventArgs e)
    {
        new MessageBoxWindow()
            // .SetMessageType(MessageBoxWindow.MessageType.Message) // Default, so you dont have to type this
            .SetTitleText("Saved Successfully!")
            .SetTag("Tag")
            .SetInfoText("The name you entered has successfully saved in the system")
            .Show();

        new MessageBoxWindow()
            .SetMessageType(MessageBoxWindow.MessageType.Warning)
            .SetTitleText("Invalid license.")
            .SetInfoText(
                "This license has no Mii data or is incomplete.\n" + "Please use the Mii Channel to create a Mii first. \n \n \n more text"
            )
            .Show();

        MessageTranslationHelper.ShowMessage(MessageTranslation.Error_StanderdError);
    }

    private async void YesNoPopup_OnClick(object sender, RoutedEventArgs e)
    {
        var yesNoWindow = await new YesNoWindow()
            .SetExtraText("text for some extra information")
            .SetMainText("Do you click yes or no")
            .AwaitAnswer();

        YesNoPopupButton.Variant = yesNoWindow ? Button.ButtonsVariantType.Primary : Button.ButtonsVariantType.Danger;
    }

    private async void OptionsPopup_OnClick(object sender, RoutedEventArgs e)
    {
        // You can do things in the action with the options.
        // However, you can also read the button click based on the title
        var optionsWindow = await new OptionsWindow()
            .AddOption("PersonMale", "Boy!", () => Console.WriteLine("Option Boy!"))
            .AddOption("PersonFemale", "Girl!", () => Console.WriteLine("Option Girl!"))
            .AddOption("Banana", "Not an Option", () => { }, false)
            .AwaitAnswer();

        OptionsPopupButton.Variant = optionsWindow != null ? Button.ButtonsVariantType.Warning : Button.ButtonsVariantType.Danger;
        OptionsPopupButton.Text = optionsWindow ?? "Clicked away";
    }

    #endregion
}
