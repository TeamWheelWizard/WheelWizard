using System.Text.Json;

namespace WheelWizard.Settings.Domain;

public class WhWzSetting : Setting
{
    private readonly Action<WhWzSetting> _saveAction;

    public WhWzSetting(Type type, string name, object defaultValue)
        : this(type, name, defaultValue, _ => { }) { }

    public WhWzSetting(Type type, string name, object defaultValue, Action<WhWzSetting> saveAction)
        : base(type, name, defaultValue)
    {
        _saveAction = saveAction;
    }

    protected override bool SetInternal(object newValue, bool skipSave = false)
    {
        var oldValue = Value;
        Value = newValue;
        var newIsValid = SaveEvenIfNotValid || IsValid();
        if (newIsValid)
        {
            if (!skipSave)
                _saveAction(this);
        }
        else
            Value = oldValue;

        return newIsValid;
    }

    public override object Get() => Value;

    public override bool IsValid() => ValidationFunc == null || ValidationFunc(Value);

    public new WhWzSetting SetValidation(Func<object?, bool> validationFunc)
    {
        base.SetValidation(validationFunc);
        return this;
    }

    public new WhWzSetting SetForceSave(bool saveEvenIfNotValid)
    {
        base.SetForceSave(saveEvenIfNotValid);
        return this;
    }

    public bool SetFromJson(JsonElement newValue, bool skipSave = false)
    {
        // Feel free to add more types if you find them
        return ValueType switch
        {
            { } t when t == typeof(bool) => Set(newValue.GetBoolean(), skipSave),
            { } t when t == typeof(int) => Set(newValue.GetInt32(), skipSave),
            { } t when t == typeof(long) => Set(newValue.GetInt64(), skipSave),
            { } t when t == typeof(float) => Set((float)newValue.GetDouble(), skipSave),
            { } t when t == typeof(double) => Set(newValue.GetDouble(), skipSave),
            { } t when t == typeof(string) => Set(newValue.GetString()!, skipSave),
            { } t when t == typeof(DateTime) => Set(newValue.GetDateTime(), skipSave),
            { IsEnum: true } t => Set(Enum.ToObject(t, newValue.GetInt32()), skipSave),
            { IsArray: true } t => SetArray(newValue, t.GetElementType()!, skipSave),
            _ => throw new InvalidOperationException($"Unsupported type: {ValueType.Name}"),
        };
    }

    private bool SetArray(JsonElement value, Type elementType, bool skipSave = false)
    {
        var json = value.GetRawText().Trim('\0');
        var arrayType = Array.CreateInstance(elementType, 0).GetType();
        var array = (Array)JsonSerializer.Deserialize(json, arrayType)!;
        return Set(array, skipSave);
    }
}
