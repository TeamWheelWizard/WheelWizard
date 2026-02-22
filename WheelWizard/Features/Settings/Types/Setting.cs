using WheelWizard.Settings;

namespace WheelWizard.Settings.Types;

public abstract class Setting
{
    protected Setting(Type type, string name, object defaultValue)
    {
        Name = name;
        DefaultValue = defaultValue;
        Value = defaultValue;
        ValueType = type;
    }

    public string Name { get; protected set; }
    public object DefaultValue { get; protected set; }
    protected object Value { get; set; }
    protected Func<object, bool>? ValidationFunc { get; set; }
    protected bool SaveEvenIfNotValid { get; set; }
    public Type ValueType { get; protected set; }

    public bool Set(object newValue, bool skipSave = false)
    {
        if (newValue.GetType() != ValueType)
            return false;

        if (Value?.Equals(newValue) == true)
            return true;

        var succeeded = SetInternal(newValue, skipSave);
        if (succeeded)
            SignalChange();

        return succeeded;
    }

    protected abstract bool SetInternal(object newValue, bool skipSave = false);

    public abstract object Get();

    public void Reset()
    {
        var s = SaveEvenIfNotValid;
        SaveEvenIfNotValid = true;
        Set(DefaultValue);
        SaveEvenIfNotValid = s;
    }

    public abstract bool IsValid();

    public Setting SetValidation(Func<object?, bool> validationFunc)
    {
        ValidationFunc = validationFunc;
        return this;
    }

    public Setting SetForceSave(bool saveEvenIfNotValid)
    {
        SaveEvenIfNotValid = saveEvenIfNotValid;
        return this;
    }

    protected void SignalChange() => SettingsSignalRuntime.Publish(this);
}
