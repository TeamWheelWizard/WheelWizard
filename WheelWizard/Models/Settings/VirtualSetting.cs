using WheelWizard.Settings;

namespace WheelWizard.Models.Settings;

public class VirtualSetting : Setting
{
    private Setting[] _dependencies;
    private readonly Action<object> _setter;
    private readonly Func<object> _getter;
    private bool _acceptsSignals = true;
    private IDisposable? _signalSubscription;

    public VirtualSetting(Type type, Action<object> setter, Func<object> getter)
        : base(type, "virtual", getter())
    {
        _dependencies = [];
        _setter = setter;
        _getter = getter;
    }

    protected override bool SetInternal(object newValue, bool skipSave = false)
    {
        // we don't use skipSave here since its a virtual setting, and so there is nothing to save
        _acceptsSignals = false;
        var oldValue = Value;
        Value = newValue;
        var newIsValid = SaveEvenIfNotValid || IsValid();
        var succeeded = false;
        if (newIsValid)
        {
            _setter(newValue);
            succeeded = true;
        }
        else
            Value = oldValue;

        _acceptsSignals = true;
        return succeeded;
    }

    public override object Get() => Value;

    // We dont have to constantly recalculate the value, since if they didn't change, the value is still the same
    // and they only change when the dependencies change, or when the users sets a new value
    public override bool IsValid() => ValidationFunc == null || ValidationFunc(Value);

    public VirtualSetting SetDependencies(params Setting[] dependencies)
    {
        // I rather not translate this message, makes it easier to check where a given error came from
        if (_dependencies.Length != 0)
            throw new ArgumentException("Dependencies have already been set once");

        _dependencies = dependencies;
        SettingsSignalRuntime.OnInitialized(signalBus =>
        {
            _signalSubscription?.Dispose();
            _signalSubscription = signalBus.Subscribe(OnSignal);
        });

        return this;
    }

    public void Recalculate()
    {
        Value = _getter();
    }

    private void OnSignal(SettingChangedSignal signal)
    {
        if (!_acceptsSignals)
            return;

        if (!_dependencies.Contains(signal.Setting))
            return;

        SignalChange();
        Recalculate();
    }
}
