# Adding a Setting in WheelWizard

This is the quick guide for adding settings with the current setup.

## Where to edit
1. `WheelWizard/Features/Settings/SettingsManager.cs`
2. `WheelWizard/Features/Settings/ISettingsServices.cs`

Note: you still touch 3 spots, but 2 are inside `SettingsManager.cs`:
1. Constructor registration
2. Public `Setting` property
3. Matching interface property

## Setting Types
- WheelWizard JSON setting: `RegisterWhWz(...)`
- Dolphin INI setting: `RegisterDolphin(...)`
- Computed (not persisted): `VirtualSetting`

## WhWz setting template
Use in `SettingsManager` constructor:

```csharp
MY_NEW_SETTING = RegisterWhWz(
    "MyNewSetting",
    false,
    value => value is bool
);
```

Add property in `SettingsManager`:

```csharp
public Setting MY_NEW_SETTING { get; }
```

Add property in `IGeneralSettings` (or `IDolphinSettings` when appropriate):

```csharp
Setting MY_NEW_SETTING { get; }
```

## Dolphin setting template
Use in `SettingsManager` constructor:

```csharp
MY_DOLPHIN_SETTING = RegisterDolphin(
    ("GFX.ini", "Settings", "MyDolphinKey"),
    0,
    value => (int)(value ?? -1) >= 0
);
```

Add property in `SettingsManager`:

```csharp
public Setting MY_DOLPHIN_SETTING { get; }
```

Add property in `IDolphinSettings`:

```csharp
Setting MY_DOLPHIN_SETTING { get; }
```

## Virtual setting template
Use when setting depends on other settings and should not be saved:

```csharp
MY_VIRTUAL_SETTING = new VirtualSetting(
    typeof(bool),
    value => { /* apply side-effects */ },
    () => { /* compute value */ return true; }
).SetDependencies(DEP_A, DEP_B);
```

## Read/Write usage
Use type-safe manager methods in callers:

```csharp
var value = settings.Get<bool>(settings.MY_NEW_SETTING);
settings.Set(settings.MY_NEW_SETTING, true);
```

## Important notes
- No `ITypedSetting` layer anymore.
- WhWz invalid/unreadable values are reset to default during load.
- Setting change notifications go through `ISettingsSignalBus`.
- Keep new logic in `Features/Settings` (not deprecated folders).

## Minimal checklist
- Register setting in constructor.
- Add public `Setting` property.
- Add interface property.
- Add validation.
- Use `Get<T>` / `Set(...)` where consumed.
