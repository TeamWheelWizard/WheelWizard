using System.Text.Json;

namespace WheelWizard.Settings.Types;

public interface IWhWzSetting
{
    string Name { get; }
    object? GetValue();
    bool SetFromJson(JsonElement value, bool skipSave = false);
    void Reset(bool skipSave = false);
}

public class WhWzSetting<T>(string name, T defaultValue, Action<IWhWzSetting>? saveAction = null)
    : Setting<T>(name, defaultValue),
        IWhWzSetting
{
    protected override void ApplyValue(bool skipSave)
    {
        if (!skipSave)
            saveAction?.Invoke(this);
    }

    object? IWhWzSetting.GetValue() => Value;

    public bool SetFromJson(JsonElement value, bool skipSave = false)
    {
        var parsed = value.Deserialize<T>();
        return parsed is not null && (!typeof(T).IsEnum || Enum.IsDefined(typeof(T), parsed)) && Set(parsed, skipSave);
    }

    public new WhWzSetting<T> SetValidation(Func<T, bool> validation)
    {
        base.SetValidation(validation);
        return this;
    }

    public new WhWzSetting<T> SetForceSave(bool enabled)
    {
        base.SetForceSave(enabled);
        return this;
    }
}
