using System.Diagnostics;

namespace WheelWizard.Settings.Types;

public abstract class Setting
{
    public event Action<Setting>? Changed;

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
        try
        {
            Set(DefaultValue);
        }
        finally
        {
            SaveEvenIfNotValid = s;
        }
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

    protected void SignalChange()
    {
        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (Action<Setting> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this);
            }
            catch (Exception exception)
            {
                // A subscriber failure must not interrupt the mutation or delivery to other subscribers.
                Trace.TraceError($"A subscriber threw while handling a change to setting '{Name}': {exception}");
            }
        }
    }
}
