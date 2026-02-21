namespace WheelWizard.Settings.Types;

public class DolphinSetting : Setting
{
    private readonly Action<DolphinSetting> _saveAction;

    public string FileName { get; private set; }
    public string Section { get; private set; }

    public DolphinSetting(Type type, (string, string, string) location, object defaultValue)
        : this(type, location, defaultValue, _ => { }) { }

    public DolphinSetting(Type type, (string, string, string) location, object defaultValue, Action<DolphinSetting> saveAction)
        : base(type, location.Item3, defaultValue)
    {
        _saveAction = saveAction;
        FileName = location.Item1;
        Section = location.Item2;
        // name/key = location.Item3

        // I rather not translate this message, makes it easier to check where a given error came from
        if (!FileName.EndsWith(".ini"))
            throw new ArgumentException(
                $"FileName for dolphin setting '[{Section}]{Name}' must end with .ini (given file is '{FileName}')"
            );
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

    public new DolphinSetting SetValidation(Func<object?, bool> validationFunc)
    {
        base.SetValidation(validationFunc);
        return this;
    }

    public new DolphinSetting SetForceSave(bool saveEvenIfNotValid)
    {
        base.SetForceSave(saveEvenIfNotValid);
        return this;
    }

    public string GetStringValue()
    {
        if (ValueType.IsEnum)
            return ((int)Value).ToString();

        return Value?.ToString() ?? "null";
    }

    public bool SetFromString(string newValue, bool skipSave = false)
    {
        // That these are the only types currently supported does not mean that these are all the Dolphin settings types
        // feel free to add more types if you find them
        return ValueType switch
        {
            { } t when t == typeof(string) => Set(newValue, skipSave),
            { } t when t == typeof(int) => Set(int.Parse(newValue), skipSave),
            { } t when t == typeof(long) => Set(long.Parse(newValue), skipSave),
            { } t when t == typeof(float) => Set(float.Parse(newValue), skipSave),
            { } t when t == typeof(double) => Set(double.Parse(newValue), skipSave),
            { } t when t == typeof(bool) => Set(bool.Parse(newValue), skipSave),
            { IsEnum: true } t => Set(Enum.ToObject(t, int.Parse(newValue)), skipSave),
            _ => throw new InvalidOperationException($"Unsupported type: {ValueType.Name}"),
        };
    }
}
