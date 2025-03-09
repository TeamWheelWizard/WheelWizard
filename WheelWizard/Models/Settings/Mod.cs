using IniParser;
using IniParser.Model;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using WheelWizard.Services;

namespace WheelWizard.Models.Settings;

public class Mod : INotifyPropertyChanged
{
    private bool _isEnabled;
    private string _title;
    private string _author;
    private int _modID;
    private int _priority; // New property for mod priority

    private bool IsLowest => _priority == ModManager.Instance.GetLowestActivePriority();
    private bool IsHighest => _priority == ModManager.Instance.GetHighestActivePriority();

    public Mod()
    {
        ModManager.Instance.PropertyChanged += OnModManagerChanged;
    }

    private void OnModManagerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModManager.Instance.Mods)) return;
        OnPropertyChanged(nameof(IsLowest));
        OnPropertyChanged(nameof(IsHighest));
    }


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

    #region PropertyChanged
    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    #endregion
    
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    /// <summary>
    /// Loads the mod details from an INI file.
    /// </summary>
    public static async Task<Mod> LoadFromIniAsync(string iniFilePath)
    {
        var parser = new FileIniDataParser();
        IniData data;
        try
        {
            data = parser.ReadFile(iniFilePath);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to read INI file '{iniFilePath}': {ex.Message}");
        }

        var mod = new Mod
        {
            Title = data["Mod"]["Name"],
            Author = data["Mod"]["Author"],
            ModID = int.TryParse(data["Mod"]["ModID"], out var id) ? id : -1,
            IsEnabled = bool.TryParse(data["Mod"]["IsEnabled"], out var enabled) ? enabled : true,
            Priority = int.TryParse(data["Mod"]["Priority"], out var priority) ? priority : 0
        };

        return await Task.FromResult(mod);
    }

    /// <summary>
    /// Saves the mod details to an INI file.
    /// </summary>
    public async Task SaveToIniAsync(string iniFilePath)
    {
        var parser = new FileIniDataParser();
        var data = new IniData();
        data["Mod"]["Name"] = this.Title;
        data["Mod"]["Author"] = this.Author;
        data["Mod"]["ModID"] = this.ModID.ToString();
        data["Mod"]["IsEnabled"] = this.IsEnabled.ToString();
        data["Mod"]["Priority"] = this.Priority.ToString();

        try
        {
            parser.WriteFile(iniFilePath, data);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to write INI file '{iniFilePath}': {ex.Message}");
        }

        await Task.CompletedTask;
    }
}


public class ModData
{
    public bool IsEnabled { get; set; }
    public string Title { get; set; }
}
