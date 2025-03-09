﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using WheelWizard.Models.Enums;
using WheelWizard.Models.MiiImages;
using WheelWizard.Services;

namespace WheelWizard.Views.Components.WhWzLibrary;

public class PlayerListItem : TemplatedControl
{

    public static readonly StyledProperty<bool> HasBadgesProperty =
        AvaloniaProperty.Register<PlayerListItem, bool>(nameof(HasBadges));
    public bool HasBadges
    {
        get => GetValue(HasBadgesProperty);
        set => SetValue(HasBadgesProperty, value);
    }
    
    public static readonly StyledProperty<Mii?> MiiProperty =
        AvaloniaProperty.Register<PlayerListItem, Mii?>(nameof(Mii));
    public Mii? Mii
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
        
    public static readonly StyledProperty<BadgeVariant[]> BadgesProperty =
        AvaloniaProperty.Register<FriendsListItem, BadgeVariant[]>(nameof(Badges));
    public BadgeVariant[] Badges
    {
        get => GetValue(BadgesProperty);
        set => SetValue(BadgesProperty, value);
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
        if (container == null) return;

        container.Children.Clear();
        foreach (var badge in Badges.Select(variant => new Badge { Variant = variant }))
        {
            badge.Height = 30;
            badge.Width = 30;
            container.Children.Add(badge);
        }
    }
}

