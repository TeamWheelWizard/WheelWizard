using System.ComponentModel;
using WheelWizard.WheelWizardData.Domain;
using WheelWizard.WiiManagement.GameLicense;
using WheelWizard.WiiManagement.MiiManagement.Domain.Mii;

namespace WheelWizard.WiiManagement.GameLicense.Domain;

public abstract class PlayerProfileBase : INotifyPropertyChanged
{
    public required string FriendCode { get; init; }
    private uint _vr;
    private uint _br;

    public required uint Vr
    {
        get => _vr;
        set
        {
            if (_vr == value)
                return;
            _vr = value;
            OnPropertyChanged(nameof(Vr));
        }
    }

    public required uint Br
    {
        get => _br;
        set
        {
            if (_br == value)
                return;
            _br = value;
            OnPropertyChanged(nameof(Br));
        }
    }
    public required uint RegionId { get; init; }
    public required Mii? Mii { get; set; }

    public string RegionName => GameLicenseDisplay.GetRegionName(RegionId);

    private bool _isOnline;
    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (value == _isOnline)
                return;
            _isOnline = value;
            OnPropertyChanged(nameof(IsOnline));
        }
    }

    public BadgeVariant[] BadgeVariants { get; set; } = [];
    public bool HasBadges => BadgeVariants.Length != 0;

    public string NameOfMii => Mii?.Name.ToString() ?? string.Empty;

    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }
    #endregion
}
