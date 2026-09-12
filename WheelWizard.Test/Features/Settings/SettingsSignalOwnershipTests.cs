using Testably.Abstractions.Testing;
using WheelWizard.Dolphin.Paths;
using WheelWizard.Settings;
using WheelWizard.Settings.Types;
using WheelWizard.Shared.Platform;
using WheelWizard.Shared.Processes;

namespace WheelWizard.Test.Features.Settings;

[Collection("SettingsFeature")]
public sealed class SettingsSignalOwnershipTests
{
    [Fact]
    public void Settings_PublishOnlyToTheirOwningBus_WithoutStartupInitialization()
    {
        var firstBus = SettingsTestUtils.CreateSettingsSignalBus();
        var secondBus = SettingsTestUtils.CreateSettingsSignalBus();
        using var first = Create(firstBus);
        using var second = Create(secondBus);
        var firstSignals = new List<Setting>();
        var secondSignals = new List<Setting>();
        using var firstSubscription = firstBus.Subscribe(signal => firstSignals.Add(signal.Setting));
        using var secondSubscription = secondBus.Subscribe(signal => secondSignals.Add(signal.Setting));

        first.ENABLE_ANIMATIONS.Set(!(bool)first.ENABLE_ANIMATIONS.Get(), skipSave: true);

        Assert.Equal(new[] { first.ENABLE_ANIMATIONS }, firstSignals);
        Assert.Empty(secondSignals);
    }

    [Fact]
    public void VirtualSettings_UpdateAndPublishThroughTheirOwner()
    {
        var bus = SettingsTestUtils.CreateSettingsSignalBus();
        using var manager = Create(bus);
        var changed = new List<Setting>();
        using var subscription = bus.Subscribe(signal => changed.Add(signal.Setting));

        Assert.True(manager.SAVED_WINDOW_SCALE.Set(2d, skipSave: true));

        Assert.Equal(2d, manager.WINDOW_SCALE.Get());
        Assert.Contains(manager.WINDOW_SCALE, changed);
        Assert.Contains(manager.SAVED_WINDOW_SCALE, changed);
    }

    [Fact]
    public void DisposingManager_DetachesSignalsAndVirtualDependencies()
    {
        var bus = SettingsTestUtils.CreateSettingsSignalBus();
        var manager = Create(bus);
        var virtualValue = manager.WINDOW_SCALE.Get();
        var count = 0;
        using var subscription = bus.Subscribe(_ => count++);

        manager.Dispose();
        manager.SAVED_WINDOW_SCALE.Set(2d, skipSave: true);

        Assert.Equal(0, count);
        Assert.Equal(virtualValue, manager.WINDOW_SCALE.Get());
    }

    private static SettingsManager Create(ISettingsSignalBus bus) =>
        new(
            Substitute.For<IWhWzSettingManager>(),
            Substitute.For<IDolphinSettingManager>(),
            Substitute.For<IRecompSettingManager>(),
            new MockFileSystem(),
            bus,
            new DolphinPathResolver(new MockFileSystem(), new RuntimeEnvironment()),
            SettingsTestUtils.CreateApplicationDataLocation(),
            SettingsTestUtils.CreateRecompPaths(),
            new RuntimeEnvironment(),
            Substitute.For<IUnixCommandService>()
        );
}
