using System.ComponentModel;
using WheelWizard.Models.Mods;

namespace WheelWizard.Mods.ViewModels;

public sealed record ModListItem(Mod Mod, bool IsLowest, bool IsHighest, ModPreviewViewModel Preview, bool HasPatches = false)
    : INotifyPropertyChanged
{
    private bool _isPlaceholder;
    public bool IsPlaceholder
    {
        get => _isPlaceholder;
        set
        {
            if (_isPlaceholder == value)
                return;
            _isPlaceholder = value;
            PropertyChanged?.Invoke(this, new(nameof(IsPlaceholder)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
