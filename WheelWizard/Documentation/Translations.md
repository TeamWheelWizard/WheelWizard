# Translations

WheelWizard translations live in `Resources/Languages` as one YAML file per language. The files are embedded into the app assembly.

```yaml
en:
  action:
    save: "Save"
```

## C#

Use `t("key")` directly. No localization import is needed.

```csharp
var text = t("action.save");
var englishText = t("en.action.save");
var message = t("snackbar_success.name_change", newName);
```

Arguments replace `{$1}`, `{$2}`, and so on.

## XAML

Add the localization namespace and use the `T` markup extension.

```xml
xmlns:loc="clr-namespace:WheelWizard.Localization"
Text="{loc:T action.save}"
```

## CSV Porting

Run `Resources/Languages/port_script.py` to export or import translations. Exports are written into the language folder.

`en.yml` is the default language and appears first in exports. Other language files are overrides. Missing keys fall back to English; keys that exist only in another language are still exported and imported for dynamic translation use.
