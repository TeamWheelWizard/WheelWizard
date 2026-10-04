using System.Diagnostics;

namespace WheelWizard.Settings.Types;

// The non-generic surface is only for change notifications and heterogeneous registries.
public abstract class Setting(string name)
{
    public string Name { get; } = name;
    public event Action<Setting>? Changed;
    public Exception? SaveError { get; protected set; }
    public abstract bool IsValid();
    public abstract void Reset(bool skipSave = false);

    protected void SignalChange()
    {
        if (Changed is not { } handlers)
            return;
        foreach (Action<Setting> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(this);
            }
            catch (Exception exception)
            {
                Trace.TraceError($"A subscriber threw while handling a change to setting '{Name}': {exception}");
            }
        }
    }
}

public abstract class Setting<T>(string name, T defaultValue) : Setting(name)
{
    public T DefaultValue { get; } = defaultValue;
    public T Value { get; protected set; } = defaultValue;
    protected bool SaveEvenIfNotValid { get; private set; }
    private Func<T, bool>? _validation;

    public T Get() => Value;

    public bool Set(T newValue, bool skipSave = false)
    {
        ArgumentNullException.ThrowIfNull(newValue);
        SaveError = null;
        if (EqualityComparer<T>.Default.Equals(Value, newValue))
            return true;
        if (!SaveEvenIfNotValid && _validation?.Invoke(newValue) == false)
            return false;

        var previous = Value;
        Value = newValue;
        try
        {
            ApplyValue(skipSave);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreValueAfterSaveFailure(previous);
            SaveError = exception;
            return false;
        }
        SignalChange();
        return true;
    }

    protected abstract void ApplyValue(bool skipSave);

    protected virtual void RestoreValueAfterSaveFailure(T previousValue) => Value = previousValue;

    public override bool IsValid() => _validation?.Invoke(Value) ?? true;

    public override void Reset(bool skipSave = false)
    {
        var previous = SaveEvenIfNotValid;
        SaveEvenIfNotValid = true;
        try
        {
            Set(DefaultValue, skipSave);
        }
        finally
        {
            SaveEvenIfNotValid = previous;
        }
    }

    public Setting<T> SetValidation(Func<T, bool> validation)
    {
        _validation = validation;
        return this;
    }

    public Setting<T> SetForceSave(bool enabled)
    {
        SaveEvenIfNotValid = enabled;
        return this;
    }
}
