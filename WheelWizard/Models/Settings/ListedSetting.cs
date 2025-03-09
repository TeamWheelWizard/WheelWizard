namespace WheelWizard.Models.Settings;

public class ListedSetting<T>
{
    public readonly Dictionary<string, T> Mapping = new Dictionary<string, T>();
    public readonly List<string> AllKeys = new List<string>();
    public readonly List<T> AllValues = new List<T>();
    public T DefaultValue { get; set; }
    
    public ListedSetting(string defaultKey, params (string, T)[] values)
    {
        foreach (var (key, value) in values)
        {
            Mapping[key] = value;
        }
        AllKeys.AddRange(Mapping.Keys);
        AllValues.AddRange(Mapping.Values);
        DefaultValue = Mapping[defaultKey];
    }
    
    public T Get(string key) => Mapping[key];
}
