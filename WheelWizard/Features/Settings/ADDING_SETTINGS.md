# Adding a Setting in WheelWizard

This guide explains how to add a setting with the current Settings feature architecture.

## Where Settings Live
- WheelWizard app settings (JSON): `WhWzSetting`
- Dolphin config settings (INI): `DolphinSetting`
- Derived/read-only computed settings: `VirtualSetting`

Main wiring is in `WheelWizard/Features/Settings/SettingsManager.cs`.

## Rules
- Add new setting logic in `Features/Settings`, not deprecated `Services/Helpers/Models/Utilities`.
- Use constructor-registered settings in `SettingsManager`.
- Use validation with `.SetValidation(...)` where possible.
- Prefer typed access (`ITypedSetting<T>`) over raw `Setting` where possible.

## 1) Add a new WhWz setting (stored in WheelWizard JSON)

### Step A: Register in `SettingsManager` constructor
```csharp
MY_NEW_SETTING = RegisterWhWz(
    CreateWhWzSetting(typeof(bool), "MyNewSetting", false)
        .SetValidation(value => value is bool)
);
```

### Step B: Expose public properties in `SettingsManager`
```csharp
public Setting MY_NEW_SETTING { get; }
public ITypedSetting<bool> MyNewSetting { get; }
```

And initialize typed wrapper in constructor:
```csharp
MyNewSetting = new TypedSetting<bool>(MY_NEW_SETTING);
```

### Step C: Add interface members in `ISettingsServices.cs`
Add raw + typed members to the correct interface (`IGeneralSettings` or `IDolphinSettings`):
```csharp
Setting MY_NEW_SETTING { get; }
ITypedSetting<bool> MyNewSetting { get; }
```

## 2) Add a new Dolphin setting (stored in Dolphin INI)

### Step A: Register in `SettingsManager` constructor
```csharp
MY_DOLPHIN_SETTING = RegisterDolphin(
    CreateDolphinSetting(typeof(int), ("GFX.ini", "Settings", "MyDolphinKey"), 0)
        .SetValidation(value => (int)(value ?? -1) >= 0)
);
```

### Step B: Expose public property in `SettingsManager`
```csharp
public Setting MY_DOLPHIN_SETTING { get; }
```

If you want typed access:
```csharp
public ITypedSetting<int> MyDolphinSetting { get; }
MyDolphinSetting = new TypedSetting<int>(MY_DOLPHIN_SETTING);
```

### Step C: Add to interface in `ISettingsServices.cs`
```csharp
Setting MY_DOLPHIN_SETTING { get; }
ITypedSetting<int> MyDolphinSetting { get; }
```

## 3) Add a derived/computed setting (`VirtualSetting`)
Use this when value depends on other settings and should not be directly persisted.

```csharp
MY_VIRTUAL_SETTING = new VirtualSetting(
    typeof(bool),
    value => { /* setter side-effects */ },
    () => { /* compute current value */ return true; }
).SetDependencies(DEP_A, DEP_B);
```

## Behavior Notes
- WhWz settings are loaded from JSON in `WhWzSettingManager`.
- If a WhWz stored value is invalid/unreadable, it is reset to default during load.
- Dolphin settings are read/written via `DolphinSettingManager` to `.ini` files.
- Setting changes publish through the settings signal bus (`ISettingsSignalBus`).

## If You Add a New Value Type
- Update JSON parsing in `WheelWizard/Models/Settings/WhWzSetting.cs` (`SetFromJson`).
- Update INI parsing in `WheelWizard/Models/Settings/DolphinSetting.cs` (`SetFromString`) if needed.

## Quick Checklist
- Register setting in `SettingsManager` constructor.
- Add public property/properties in `SettingsManager`.
- Add interface members in `ISettingsServices.cs`.
- Add validation.
- Use typed setting in calling code.
