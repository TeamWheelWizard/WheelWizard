﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using WheelWizard.Models.MiiImages;
using WheelWizard.Services;


namespace WheelWizard.Views.Components;

public class PlayerListItem : TemplatedControl
{

    public static readonly StyledProperty<bool> HasBadgesProperty =
        AvaloniaProperty.Register<PlayerListItem, bool>(nameof(HasBadges));
    public bool HasBadges
    {
        get => GetValue(HasBadgesProperty);
        set => SetValue(HasBadgesProperty, value);
    }
    
    public static readonly StyledProperty<FullMii?> MiiProperty =
        AvaloniaProperty.Register<PlayerListItem, FullMii?>(nameof(Mii));
    public FullMii? Mii
    {
        get => GetValue(MiiProperty);
        set => SetValue(MiiProperty, value);
    }
    
    
    public static readonly StyledProperty<string> VrProperty =
        AvaloniaProperty.Register<PlayerListItem, string>(nameof(Vr));
    public string Vr
    {
        get => GetValue(VrProperty);
        set => SetValue(VrProperty, value);
    }
    public static readonly StyledProperty<string> BrProperty =
        AvaloniaProperty.Register<PlayerListItem, string>(nameof(Br));
    public string Br
    {
        get => GetValue(BrProperty);
        set => SetValue(BrProperty, value);
    }
    
    public static readonly StyledProperty<string> FriendCodeProperty =
        AvaloniaProperty.Register<PlayerListItem, string>(nameof(FriendCode));
    public string FriendCode
    {
        get => GetValue(FriendCodeProperty);
        set => SetValue(FriendCodeProperty, value);
    }
    
    public static readonly StyledProperty<string> UserNameProperty =
        AvaloniaProperty.Register<PlayerListItem, string>(nameof(UserName));
    public string UserName
    {
        get => GetValue(UserNameProperty);
        set => SetValue(UserNameProperty, value);
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
    }
}

