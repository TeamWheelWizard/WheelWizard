# Adding a Setting in WheelWizard

## Setting Types
- **WheelWizard:** Our own settings, we save them in a JSON file.
- **Dolphin:** Settings from the Dolphin emulator. they store them in INI files. this implementation allows us to also modify them=
- **Virtual:** Settings that are not saved. These are used for managing for computing state and managing side effect. For instance, if you want to control 3 settings with 1 toggle, virtual settings is perfect for that.

## Adding settings
You first always define the setting in the `ISettingsServices.cs` file in the `ISettingsProperties` class
```csharp
Setting MY_NEW_SETTING { get; }
```
then you also define this setting in the `SettingsManager.cs` as a property
```csharp
public Setting MY_NEW_SETTING { get; }
```

after that you have to register the setting. This depends on the type of setting you want to add.

### Wheel Wizard
```csharp
MY_NEW_SETTING = RegisterWhWz(
    "MyNewSetting",
    false,
    value => value is bool
);
```

### Dolphin
```csharp
MY_DOLPHIN_SETTING = RegisterDolphin(
    ("GFX.ini", "Settings", "MyDolphinKey"),
    0,
    value => (int)(value ?? -1) >= 0
);
```

### Virtual
```csharp
MY_VIRTUAL_SETTING = new VirtualSetting(
    typeof(bool),
    value => { /* apply side-effects */ },
    () => { /* compute value */ return true; }
).SetDependencies(SETTING_A, SETTING_B);
```
Usually you create virtual settigns that reference one or more real settings.
The value of the virtual setting is cached. However, if the value relies on e.g. SETTING_A. than once SETTING_A changes, your cache is wrong.
// For that reason, you have to set dependencies. That way if SETTING_A changes, the virtual setting gets a signal to recompute its value


## Reading/Writing settings
Use type-safe manager methods in callers:
```csharp
// reading
bool value = SettingsManager.Get<bool>(SettingsManager.MY_NEW_SETTING);
// writeing
SettingsManager.Set(SettingsManager.MY_NEW_SETTING, true);
```
