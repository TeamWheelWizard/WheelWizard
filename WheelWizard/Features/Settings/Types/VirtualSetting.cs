using WheelWizard.Settings;

namespace WheelWizard.Settings.Types;

public class VirtualSetting<T> : Setting<T>, IDisposable
{
    private Setting[] _dependencies = [];
    private readonly Action<T> _setter;
    private readonly Func<T> _getter;
    private bool _acceptsSignals = true;
    private bool _dependenciesAssigned;

    public VirtualSetting(Action<T> setter, Func<T> getter)
        : base("virtual", getter())
    {
        _setter = setter;
        _getter = getter;
    }

    protected override void ApplyValue(bool skipSave)
    {
        _acceptsSignals = false;
        try
        {
            _setter(Value);
        }
        finally
        {
            _acceptsSignals = true;
        }
    }

    public VirtualSetting<T> SetDependencies(params Setting[] dependencies)
    {
        // I rather not translate this message, makes it easier to check where a given error came from
        if (_dependenciesAssigned)
            throw new ArgumentException("Dependencies have already been set once");

        _dependencies = dependencies;
        _dependenciesAssigned = true;
        foreach (var dependency in _dependencies)
            dependency.Changed += OnDependencyChanged;

        return this;
    }

    public void Recalculate()
    {
        Value = _getter();
    }

    private void OnDependencyChanged(Setting dependency)
    {
        if (!_acceptsSignals)
            return;

        Recalculate();
        SignalChange();
    }

    public void Dispose()
    {
        foreach (var dependency in _dependencies)
            dependency.Changed -= OnDependencyChanged;
        _dependencies = [];
    }
}
