using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using System.ComponentModel;
using WheelWizard.Models.MiiImages;
using WheelWizard.Models.Settings;
using WheelWizard.Resources.Languages;
using WheelWizard.Services.WiiManagement.SaveData;
using WheelWizard.Views.Pages;

namespace WheelWizard.Views.Components.WhWzLibrary;

public class CurrentUserProfile : TemplatedControl, INotifyPropertyChanged
{
    public static readonly StyledProperty<string> FriendCodeProperty =
        AvaloniaProperty.Register<CurrentUserProfile, string>(nameof(FriendCode));
    public string FriendCode
    {
        get => GetValue(FriendCodeProperty);
        set => SetValue(FriendCodeProperty, value);
    }
    
    public static readonly StyledProperty<string> UserNameProperty =
        AvaloniaProperty.Register<CurrentUserProfile, string>(nameof(UserName));
    public string UserName
    {
        get => GetValue(UserNameProperty);
        set => SetValue(UserNameProperty, value);
    }
    
    public static readonly StyledProperty<Mii?> MiiProperty =
        AvaloniaProperty.Register<CurrentUserProfile, Mii?>(nameof(Mii));
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

        GameDataLoader.Instance.RefreshOnlineStatus();
        var currentUser = GameDataLoader.Instance.GetCurrentUser;
        
        var name = currentUser.MiiName;
        if (name == SettingValues.NoName)
            name = Online.NoName;
        if (name == SettingValues.NoLicense)
            name = Online.NoLicense;
        
        UserName = name;
        FriendCode = currentUser.FriendCode;
        Mii = currentUser.Mii;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e) => ViewUtils.NavigateToPage(new UserProfilePage());
    
    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}

