using WheelWizard.Models.Settings;

namespace WheelWizard.Settings;

public sealed class TypedSetting<T> : ITypedSetting<T>
{
    public TypedSetting(Setting rawSetting)
    {
        RawSetting = rawSetting;
    }

    public string Name => RawSetting.Name;
    public Setting RawSetting { get; }

    public T Get() => (T)RawSetting.Get();

    public bool IsValid() => RawSetting.IsValid();

    public bool Set(T value, bool skipSave = false)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        return RawSetting.Set(value, skipSave);
    }
}
