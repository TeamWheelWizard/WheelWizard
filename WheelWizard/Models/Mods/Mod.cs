using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WheelWizard.Models.Mods;

public class Mod : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _title = string.Empty;
    private string _author = string.Empty;
    private int _modID;
    private int _priority; // New property for mod priority
    private bool _hasIncompatibleFiles;

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value)
                return;

            _isEnabled = value;
            OnPropertyChanged(nameof(IsEnabled));
        }
    }

    public string Title
    {
        get => _title;
        set
        {
            if (_title == value)
                return;

            _title = value;
            OnPropertyChanged(nameof(Title));
        }
    }

    public string Author
    {
        get => _author;
        set
        {
            if (_author == value)
                return;

            _author = value;
            OnPropertyChanged(nameof(Author));
        }
    }

    public int ModID
    {
        get => _modID;
        set
        {
            if (_modID == value)
                return;

            _modID = value;
            OnPropertyChanged(nameof(ModID));
        }
    }

    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority == value)
                return;

            _priority = value;
            OnPropertyChanged(nameof(Priority));
        }
    }

    public bool HasIncompatibleFiles
    {
        get => _hasIncompatibleFiles;
        set
        {
            if (_hasIncompatibleFiles == value)
                return;

            _hasIncompatibleFiles = value;
            OnPropertyChanged(nameof(HasIncompatibleFiles));
        }
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged(string? propertyName)
    {
        PropertyChanged?.Invoke(this, new(propertyName));
    }
    #endregion
}

public class ModData
{
    public bool IsEnabled { get; set; }
    public string Title { get; set; } = string.Empty;
}
