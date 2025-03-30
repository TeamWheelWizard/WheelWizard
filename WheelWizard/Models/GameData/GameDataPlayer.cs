﻿using System.ComponentModel;
using WheelWizard.Helpers;
using WheelWizard.Models.Enums;
using WheelWizard.Models.MiiImages;
using WheelWizard.Models.Settings;
using WheelWizard.Services;
using WheelWizard.Services.LiveData;
using WheelWizard.WiiManagement;
using WheelWizard.WiiManagement.Domain.Enums;

namespace WheelWizard.Models.GameData;

public abstract class GameDataPlayer : INotifyPropertyChanged
{
    public required string FriendCode { get; init; }
    public required uint Vr { get; init; }
    public required uint Br { get; init; }
    public required uint RegionId { get; init; } 
    public required MiiData? MiiData { get; set; }
    
    public string RegionName => Humanizer.GetRegionName(RegionId);
    public FullMii? Mii => MiiData?.Mii;
    
    public bool IsOnline
    {
        get
        {
            var currentRooms = RRLiveRooms.Instance.CurrentRooms;
            if (currentRooms.Count <= 0) 
                return false;

            var onlinePlayers = currentRooms.SelectMany(room => room.Players.Values).ToList();
            return onlinePlayers.Any(player => player.Fc == FriendCode);
        }
        set
        {
            if (value == IsOnline) 
                return;
            
            OnPropertyChanged(nameof(IsOnline));
        }
    }

    public BadgeVariant[] BadgeVariants => BadgeManager.Instance.GetBadgeVariants(FriendCode);
    public bool HasBadges => BadgeVariants.Length != 0;
    
    public string NameOfMii => Mii?.Name.ToString() ?? string.Empty;

    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
}
