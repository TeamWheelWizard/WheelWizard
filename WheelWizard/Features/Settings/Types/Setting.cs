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
    public Exception? SaveError { get; private set; }

    public bool Set(object newValue, bool skipSave = false)
    {
        SaveError = null;
        if (newValue.GetType() != ValueType)
            return false;

        if (Value.Equals(newValue))
            return true;

        var previousValue = Value;
        bool succeeded;
        try
        {
            succeeded = SetInternal(newValue, skipSave);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreValueAfterSaveFailure(previousValue);
            SaveError = exception;
            return false;
        }
        if (succeeded)
            SignalChange();

        return succeeded;
    }

    protected abstract bool SetInternal(object newValue, bool skipSave = false);

    protected virtual void RestoreValueAfterSaveFailure(object previousValue) => Value = previousValue;

    public abstract object Get();

    public void Reset(bool skipSave = false)
    {
        var s = SaveEvenIfNotValid;
        SaveEvenIfNotValid = true;
        try
        {
            Set(DefaultValue, skipSave);
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
