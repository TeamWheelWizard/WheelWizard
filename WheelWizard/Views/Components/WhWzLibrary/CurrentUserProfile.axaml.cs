using System.ComponentModel;
using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Shared.DependencyInjection;
using WheelWizard.Views.Pages;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.Domain.Mii;

namespace WheelWizard.Views.Components;

public class CurrentUserProfile : UserControlBase, INotifyPropertyChanged
{
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
        set
        {
            SetValue(MiiProperty, value);
            OnPropertyChanged(nameof(Mii));
        }
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        GameLicenseService.RefreshOnlineStatus();
        var currentUser = GameLicenseService.ActiveUser;

        var name = currentUser.NameOfMii;
        if (name == SettingValues.NoName)
            name = Online.NoName;
        if (name == SettingValues.NoLicense)
            name = Online.NoLicense;

        UserName = name;
        FriendCode = currentUser.FriendCode;
        Mii = currentUser.Mii;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) => NavigationManager.NavigateTo<UserProfilePage>();

    #region PropertyChanged

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }

    #endregion
}
