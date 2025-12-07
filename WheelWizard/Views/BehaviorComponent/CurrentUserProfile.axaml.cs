using Avalonia;
using Avalonia.Input;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Pages;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.BehaviorComponent;

public partial class CurrentUserProfile : UserControlBase
{
    #region Properties

    [Inject]
    private IGameLicenseSingletonService GameLicenseService { get; set; } = null!;

    public static readonly StyledProperty<string> FriendCodeProperty = AvaloniaProperty.Register<CurrentUserProfile, string>(
        nameof(FriendCode)
    );

    public string FriendCode
    {
        get => GetValue(FriendCodeProperty);
        set => SetValue(FriendCodeProperty, value);
    }

    public static readonly StyledProperty<string> UserNameProperty = AvaloniaProperty.Register<CurrentUserProfile, string>(
        nameof(UserName)
    );

    public string UserName
    {
        get => GetValue(UserNameProperty);
        set => SetValue(UserNameProperty, value);
    }

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<CurrentUserProfile, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    #endregion

    public CurrentUserProfile()
    {
        InitializeComponent();
        DataContext = this;

        GameLicenseService.RefreshOnlineStatus();
        GameLicenseService.LoadLicense();

        var currentUser = GameLicenseService.ActiveUser;

        var name = currentUser.NameOfMii;
        if (name == SettingValues.NoName)
            name = Common.State_NoName;
        if (name == SettingValues.NoLicense)
            name = Common.State_NoLicense;

        UserName = name;
        FriendCode = currentUser.FriendCode;
        Mii = currentUser.Mii;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) => NavigationManager.NavigateTo<UserProfilePage>();
}
