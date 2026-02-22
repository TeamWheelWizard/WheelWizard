using WheelWizard.Settings;
using WheelWizard.Settings.Types;

namespace WheelWizard.Test.Features.Settings;

[Collection("SettingsFeature")]
public class VirtualSettingTests
{
    [Fact]
    public void Set_StoresValueAndInvokesSetter_WhenValueIsValid()
    {
        var backingValue = 1;
        var setting = new VirtualSetting(typeof(int), value => backingValue = (int)value, () => backingValue);

        var result = setting.Set(5);

        Assert.True(result);
        Assert.Equal(5, backingValue);
        Assert.Equal(5, Assert.IsType<int>(setting.Get()));
    }

    [Fact]
    public void Set_ReturnsFalseAndKeepsOldValue_WhenValidationFails()
    {
        var backingValue = 2;
        var setting = new VirtualSetting(typeof(int), value => backingValue = (int)value, () => backingValue).SetValidation(value =>
            (int)value! >= 0
        );

        var result = setting.Set(-1);

        Assert.False(result);
        Assert.Equal(2, backingValue);
        Assert.Equal(2, Assert.IsType<int>(setting.Get()));
    }

    [Fact]
    public void SetDependencies_RecalculatesValue_WhenDependencySignalsChange()
    {
        SettingsTestUtils.InitializeSignalRuntime(new SettingsSignalBus());
        var dependency = new WhWzSetting(typeof(int), "Dependency", 1);
        var setting = new VirtualSetting(typeof(int), _ => { }, () => (int)dependency.Get()).SetDependencies(dependency);

        dependency.Set(7, skipSave: true);

        Assert.Equal(7, Assert.IsType<int>(setting.Get()));
    }

    [Fact]
    public void SetDependencies_Throws_WhenCalledTwice()
    {
        var dependency = new WhWzSetting(typeof(int), "Dependency", 1);
        var setting = new VirtualSetting(typeof(int), _ => { }, () => 1).SetDependencies(dependency);

        Assert.Throws<ArgumentException>(() => setting.SetDependencies(dependency));
    }
}
