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
var message = t("snackbar_success.name_change", new { name = newName });
```

Anonymous-object properties replace named placeholders such as `%{name}` and `%{version}`. Names are case-sensitive. Missing arguments leave their placeholders unchanged; null values become empty text.

```yaml
items:
  one: "%{name} has one item"
  other: "%{name} has %{count} items"
```

```csharp
var message = t("items", count: 12, new { name = "Alex" });
```

`count` selects the plural form and automatically fills `%{count}`: exactly 1 selects `one`, and every other value selects `other`. The explicit count argument takes precedence over an anonymous-object property named `count`.

Use `.one` and `.other` for quantities, for example `t("time.days", count: days)`.

An optional `.none` key describes an empty state. Callers must explicitly select it when appropriate: `amount == 0 ? t("items.none") : t("items", count: amount)`. Localization never selects `.none` automatically; a count of zero selects `.other`.

`tFormat(text, new { name = "Alex" })` formats an already translated string.

## XAML

Add the localization namespace and use the `T` markup extension.

```xml
xmlns:loc="clr-namespace:WheelWizard.Localization"
Text="{loc:T action.save}"
```

## CSV Porting

Run `Resources/Languages/port_script.py` to export or import translations. Exports are written into the language folder.

`en.yml` is the default language and appears first in exports. Other language files are overrides. Missing keys fall back to English; keys that exist only in another language are still exported and imported for dynamic translation use.
