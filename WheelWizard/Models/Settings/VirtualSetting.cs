namespace WheelWizard.Models.Settings;

public class VirtualSetting : Setting, ISettingListener
{
    private Setting[] _dependencies;
    private Action<object> Setter;
    private Func<object> Getter;
    private bool _acceptsSignals = true;
    
    public VirtualSetting(Type type, Action<object> setter, Func<object> getter) 
        : base(type, "virtual", getter())
    {
        _dependencies = Array.Empty<Setting>();
        Setter = setter;
        Getter = getter;
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
            Setter(newValue);
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
    public override bool IsValid()  => ValidationFunc == null || ValidationFunc(Value);
    
    public VirtualSetting SetDependencies(params Setting[] dependencies)
    {
        // I rather not translate this message, makes it easier to check where a given error came from
        if (_dependencies.Length != 0)
            throw new ArgumentException("Dependencies have already been set once");
        
        _dependencies = dependencies;
        foreach (var dependency in dependencies)
        {
            dependency.Subscribe(this);
        }

        return this;
    }
    
    public void Recalculate()
    {
        Value = Getter();
    }
    
    public void OnSettingChanged(Setting changedSetting)
    {
        if (!_acceptsSignals)
            return;
        
        SignalChange();
        Recalculate();
    }
}
