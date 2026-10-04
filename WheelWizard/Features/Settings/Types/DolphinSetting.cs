using System.Globalization;

namespace WheelWizard.Settings.Types;

public interface IDolphinSetting
{
    string Name { get; }
    string FileName { get; }
    string Section { get; }
    string GetStringValue();
    bool SetFromString(string value, bool skipSave = false);
    void Reset(bool skipSave = false);
}

public class DolphinSetting<T> : Setting<T>, IDolphinSetting
{
    private readonly Action<IDolphinSetting>? _save;
    public string FileName { get; }
    public string Section { get; }

    public DolphinSetting((string File, string Section, string Key) location, T defaultValue, Action<IDolphinSetting>? saveAction = null)
        : base(location.Key, defaultValue)
    {
        if (!location.File.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Dolphin settings must use an .ini file.");
        FileName = location.File;
        Section = location.Section;
        _save = saveAction;
    }

    protected override void ApplyValue(bool skipSave)
    {
        if (!skipSave)
            _save?.Invoke(this);
    }

    public string GetStringValue() =>
        typeof(T).IsEnum
            ? Convert.ToInt32(Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : Convert.ToString(Value, CultureInfo.InvariantCulture) ?? string.Empty;

    public bool SetFromString(string value, bool skipSave = false)
    {
        try
        {
            // Runtime conversion belongs at the untrusted file boundary, never at a setting call site.
            if (typeof(T).IsEnum)
                return int.TryParse(value, out var number)
                    && Enum.IsDefined(typeof(T), number)
                    && Set((T)Enum.ToObject(typeof(T), number), skipSave);
            return Set((T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture), skipSave);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or InvalidCastException)
        {
            return false;
        }
    }

    public new DolphinSetting<T> SetValidation(Func<T, bool> validation)
    {
        base.SetValidation(validation);
        return this;
    }

    public new DolphinSetting<T> SetForceSave(bool enabled)
    {
        base.SetForceSave(enabled);
        return this;
    }
}
