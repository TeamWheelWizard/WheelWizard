using Avalonia;
using Avalonia.Input;
using WheelWizard.Settings.Types;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Patterns;

public partial class CurrentUserProfile : UserControlBase
{
    #region Properties

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
    }

    public void DisplayProfile(string name, string friendCode, Mii? mii)
    {
        if (name == SettingValues.NoName)
            name = t("state.no_name");
        if (name == SettingValues.NoLicense)
            name = t("state.no_license");

        UserName = name;
        FriendCode = friendCode;
        Mii = mii;
    }

    public event EventHandler? ProfileRequested;

    protected override void OnPointerPressed(PointerPressedEventArgs e) => ProfileRequested?.Invoke(this, EventArgs.Empty);
}
