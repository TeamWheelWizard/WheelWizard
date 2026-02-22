using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using WheelWizard.WheelWizardData;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.Views.Components;

public class PlayerListItem : TemplatedControl
{
    private Button? _joinRoomButton;

    public static readonly StyledProperty<bool> IsOnlineProperty = AvaloniaProperty.Register<PlayerListItem, bool>(nameof(IsOnline));
    public static readonly StyledProperty<bool> ShowJoinRoomButtonProperty = AvaloniaProperty.Register<PlayerListItem, bool>(
        nameof(ShowJoinRoomButton)
    );

    public static readonly StyledProperty<bool> HasBadgesProperty = AvaloniaProperty.Register<PlayerListItem, bool>(nameof(HasBadges));

    public static readonly StyledProperty<bool> IsOpenHostProperty = AvaloniaProperty.Register<PlayerListItem, bool>(nameof(IsOpenHost));

    public static readonly StyledProperty<bool> IsTopPlayerProperty = AvaloniaProperty.Register<PlayerListItem, bool>(nameof(IsTopPlayer));

    public static readonly StyledProperty<string> TopLabelProperty = AvaloniaProperty.Register<PlayerListItem, string>(
        nameof(TopLabel),
        string.Empty
    );

    public bool HasBadges
    {
        get => GetValue(HasBadgesProperty);
        set => SetValue(HasBadgesProperty, value);
    }

    public bool IsOnline
    {
        get => GetValue(IsOnlineProperty);
        set => SetValue(IsOnlineProperty, value);
    }

    public bool ShowJoinRoomButton
    {
        get => GetValue(ShowJoinRoomButtonProperty);
        set => SetValue(ShowJoinRoomButtonProperty, value);
    }

    public bool IsOpenHost
    {
        get => GetValue(IsOpenHostProperty);
        set => SetValue(IsOpenHostProperty, value);
    }

    public bool IsTopPlayer
    {
        get => GetValue(IsTopPlayerProperty);
        set => SetValue(IsTopPlayerProperty, value);
    }

    public string TopLabel
    {
        get => GetValue(TopLabelProperty);
        set => SetValue(TopLabelProperty, value);
    }

    public static readonly StyledProperty<Mii?> MiiProperty = AvaloniaProperty.Register<PlayerListItem, Mii?>(nameof(Mii));

    public Mii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }

    public static readonly StyledProperty<string> VrProperty = AvaloniaProperty.Register<PlayerListItem, string>(nameof(Vr));

    public string Vr
    {
        get => GetValue(VrProperty);
        set => SetValue(VrProperty, value);
    }

    public static readonly StyledProperty<string> BrProperty = AvaloniaProperty.Register<PlayerListItem, string>(nameof(Br));

    public string Br
    {
        get => GetValue(BrProperty);
        set => SetValue(BrProperty, value);
    }

    public static readonly StyledProperty<string> FriendCodeProperty = AvaloniaProperty.Register<PlayerListItem, string>(
        nameof(FriendCode)
    );

    public string FriendCode
    {
        get => GetValue(FriendCodeProperty);
        set => SetValue(FriendCodeProperty, value);
    }

    public static readonly StyledProperty<string> UserNameProperty = AvaloniaProperty.Register<PlayerListItem, string>(nameof(UserName));

    public string UserName
    {
        get => GetValue(UserNameProperty);
        set => SetValue(UserNameProperty, value);
    }

    public static readonly StyledProperty<Action<string>?> JoinRoomActionProperty = AvaloniaProperty.Register<
        PlayerListItem,
        Action<string>?
    >(nameof(JoinRoomAction));

    public Action<string>? JoinRoomAction
    {
        get => GetValue(JoinRoomActionProperty);
        set => SetValue(JoinRoomActionProperty, value);
    }

    private void JoinRoom_OnClick(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FriendCode))
            return;

        JoinRoomAction?.Invoke(FriendCode);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        var container = e.NameScope.Find<StackPanel>("PART_BadgeContainer");
        if (container != null)
        {
            container.Children.Clear();
            var badges = App
                .Services.GetRequiredService<IWhWzDataSingletonService>()
                .GetBadges(FriendCode)
                .Select(variant => new Badge { Variant = variant });
            foreach (var badge in badges)
            {
                badge.Height = 30;
                badge.Width = 30;
                container.Children.Add(badge);
            }
        }

        if (_joinRoomButton != null)
            _joinRoomButton.Click -= JoinRoom_OnClick;

        _joinRoomButton = e.NameScope.Find<Button>("PART_JoinRoomButton");
        if (_joinRoomButton != null)
            _joinRoomButton.Click += JoinRoom_OnClick;
    }
}
