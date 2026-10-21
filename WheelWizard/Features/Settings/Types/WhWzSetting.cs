using System.Text.Json;

namespace WheelWizard.Settings.Types;

public interface IWhWzSetting
{
    string Name { get; }
    object? GetValue();
    bool SetFromJson(JsonElement value, bool skipSave = false);

    /// <summary>Remembers a stored value the setting could not take (see <see cref="Setting{T}.UnavailableValue"/>).</summary>
    void RememberUnavailable(JsonElement value);
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

    // Until a value is picked again, a choice that could not be used at load (an unplugged drive) is written back as it was.
    object? IWhWzSetting.GetValue() =>
        HasUnavailableValue && EqualityComparer<T>.Default.Equals(Value, DefaultValue) ? UnavailableValue : Value;

    void IWhWzSetting.RememberUnavailable(JsonElement value)
    {
        try
        {
            if (value.Deserialize<T>() is { } parsed)
                KeepUnavailable(parsed);
        }
        catch (JsonException) { }
    }

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
