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
    private bool _retainsUnavailable;

    /// <summary>
    /// What was stored for this setting when it was loaded but could not be used then, e.g. a folder on a drive that
    /// is not connected. It is never the setting's value. It tells that something was configured, and it is what
    /// gets written back until a new value is set, so an absent drive does not erase the choice.
    /// </summary>
    public T? UnavailableValue => _hasUnavailable ? _unavailable : default;

    // Tracked separately: for a value type, T? is plain T, so default(T) cannot stand for "nothing remembered".
    public bool HasUnavailableValue => _hasUnavailable && !EqualityComparer<T>.Default.Equals(_unavailable, DefaultValue);

    private T _unavailable = defaultValue;
    private bool _hasUnavailable;

    public T Get() => Value;

    public bool Set(T newValue, bool skipSave = false)
    {
        ArgumentNullException.ThrowIfNull(newValue);
        SaveError = null;
        if (EqualityComparer<T>.Default.Equals(Value, newValue))
        {
            _hasUnavailable = false;
            return true;
        }
        if (!SaveEvenIfNotValid && _validation?.Invoke(newValue) == false)
            return false;

        // Only a value that is taken replaces the remembered one; a rejected or unsaved one leaves it as it was.
        var previous = Value;
        var hadUnavailable = _hasUnavailable;
        Value = newValue;
        _hasUnavailable = false;
        try
        {
            ApplyValue(skipSave);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RestoreValueAfterSaveFailure(previous);
            _hasUnavailable = hadUnavailable;
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

    /// <summary>Opts in to <see cref="UnavailableValue"/>; meant for settings that point at something outside the app.</summary>
    public Setting<T> SetRetainUnavailable()
    {
        _retainsUnavailable = true;
        return this;
    }

    /// <summary>For the loaders, right after a stored value was rejected and the default put in its place.</summary>
    public void KeepUnavailable(T value)
    {
        if (!_retainsUnavailable)
            return;
        _unavailable = value;
        _hasUnavailable = true;
    }
}
