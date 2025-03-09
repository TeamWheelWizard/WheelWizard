using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using WheelWizard.Models.MiiImages;
using WheelWizard.Services;

namespace WheelWizard.Views.Components.WhWzLibrary;

public class FriendsListItem : TemplatedControl
{
    public static readonly StyledProperty<bool> IsOnlineProperty =
        AvaloniaProperty.Register<FriendsListItem, bool>(nameof(IsOnline));
    public bool IsOnline
    {
        get => GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }
    
    public static readonly StyledProperty<bool> HasBadgesProperty =
        AvaloniaProperty.Register<FriendsListItem, bool>(nameof(HasBadges));
    public bool HasBadges
    {
        get => GetValue(HasBadgesProperty);
        set => SetValue(HasBadgesProperty, value);
    }
    
    public static readonly StyledProperty<Mii?> MiiProperty =
        AvaloniaProperty.Register<FriendsListItem, Mii?>(nameof(Mii));
    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }
    
    public static readonly StyledProperty<string> TotalWonProperty =
        AvaloniaProperty.Register<FriendsListItem, string>(nameof(TotalWon));
    public string TotalWon
    {
        get => GetValue(TotalWonProperty);
        set => SetValue(TotalWonProperty, value);
    }
    public static readonly StyledProperty<string> TotalLossesProperty =
        AvaloniaProperty.Register<FriendsListItem, string>(nameof(TotalLosses));
    public string TotalLosses
    {
        get => GetValue(TotalLossesProperty);
        set => SetValue(TotalLossesProperty, value);
    }
    
    public static readonly StyledProperty<string> VrProperty =
        AvaloniaProperty.Register<FriendsListItem, string>(nameof(Vr), coerce: CoerceVrAndBr);
    public string Vr
    {
        get => GetValue(VrProperty);
        set => SetValue(VrProperty, value);
    }
    public static readonly StyledProperty<string> BrProperty =
        AvaloniaProperty.Register<FriendsListItem, string>(nameof(Br), coerce: CoerceVrAndBr);
    public string Br
    {
        get => GetValue(BrProperty);
        set => SetValue(BrProperty, value);
    }
    
    private static string CoerceVrAndBr(AvaloniaObject o, string value) => value == "9999" ? "9999+" : value;
    
    public static readonly StyledProperty<string> FriendCodeProperty =
        AvaloniaProperty.Register<FriendsListItem, string>(nameof(FriendCode));
    public string FriendCode
    {
        get => GetValue(FriendCodeProperty);
        set => SetValue(FriendCodeProperty, value);
    }
    
    public static readonly StyledProperty<string> UserNameProperty =
        AvaloniaProperty.Register<FriendsListItem, string>(nameof(UserName));
    public string UserName
    {
        get => GetValue(UserNameProperty);
        set => SetValue(UserNameProperty, value);
    }
    
    public static readonly StyledProperty< Action<string>?> ViewRoomActionProperty =
        AvaloniaProperty.Register<FriendsListItem,  Action<string>?>(nameof(ViewRoomAction));
    public  Action<string>? ViewRoomAction
    {
        get => GetValue(ViewRoomActionProperty);
        set => SetValue(ViewRoomActionProperty, value);
    }

    public void ViewRoom(object? sender, RoutedEventArgs e)
    {
        ViewRoomAction.Invoke(FriendCode);
    }
    
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        var container = e.NameScope.Find<StackPanel>("PART_BadgeContainer");
        if (container != null)
        {
            container.Children.Clear();
            var badges = BadgeManager.Instance.GetBadges(FriendCode);
            foreach (var badge in badges)
            {
                badge.Height = 30;
                badge.Width = 30;
                container.Children.Add(badge);
            }
        }
        
        var viewRoomButton = e.NameScope.Find<StandardLibrary.Button>("ViewRoomButton");
        if (viewRoomButton != null)
            viewRoomButton.Click += ViewRoom;
    }
}

