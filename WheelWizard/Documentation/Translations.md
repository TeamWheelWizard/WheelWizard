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
  other: "%{name} has %{amount} items"
```

```csharp
var message = t("items", count: 12, new { name = "Alex", amount = 12 });
```

`count` is reserved for plural selection: exactly 1 selects `one`, and every other value selects `other`. Do not use `%{count}` as a placeholder; pass a separate named argument such as `amount` when displaying the number.

Existing numeric translation keys (`.0`, `.1`, `.n`) use `t_legacy("time.days.n", count: days, new { amount = days })`. Both APIs use the same named-placeholder syntax. `tFormat(text, new { name = "Alex" })` formats an already translated string.

## XAML

Add the localization namespace and use the `T` markup extension.

```xml
xmlns:loc="clr-namespace:WheelWizard.Localization"
Text="{loc:T action.save}"
```

## CSV Porting

Run `Resources/Languages/port_script.py` to export or import translations. Exports are written into the language folder.

`en.yml` is the default language and appears first in exports. Other language files are overrides. Missing keys fall back to English; keys that exist only in another language are still exported and imported for dynamic translation use.
