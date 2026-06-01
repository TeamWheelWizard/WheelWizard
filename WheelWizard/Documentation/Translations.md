# Translations

WheelWizard translations live in `Resources/Languages` as one YAML file per language:

```yaml
en:
  action:
    cancel: "Cancel"
    link:
      github: "GitHub"
```

Use the two-letter language code as the root key. Translation keys are lowercase nested YAML paths. The old key `Action_Link_Github` maps to `action.link.github`.
PascalCase inside a segment becomes snake_case, so `EmptyContent_NoFriends_Title` maps to `empty_content.no_friends.title`.

## C# and XAML

Most app code can keep using the compatibility classes:

```csharp
using WheelWizard.Resources.Languages;

var text = Common.Action_Cancel;
```

```xml
xmlns:lang="clr-namespace:WheelWizard.Resources.Languages"
Text="{x:Static lang:Common.Action_Cancel}"
```

The generated compatibility classes live in `Features/Localization/Generated` and call the YAML localization service. New localization logic belongs in `Features/Localization`, not in `Resources/Languages`.

## Adding Text

Add the same YAML path to every language file. If a legacy scalar key also has child option keys, keep the option keys beside the scalar instead of nesting under it, for example:

```yaml
en:
  attribute:
    mii:
      gender: "Gender"
      gender_female: "Female"
      gender_male: "Male"
```

`en.yml` is the source of truth for translation keys. Other language files are overrides:

- Missing keys fall back to `en.yml`.
- Keys that do not exist in `en.yml` are ignored by the app and by the CSV import/export script.

## Adding Languages

1. Add `Resources/Languages/<code>.yml`.
2. Use `<code>:` as the root key.
3. Add the language to `LocalizationLanguageCatalog.SupportedLanguages` if it should appear in the settings dropdown.
4. Add the language name translation at `value.language.<name>`.

Native language names are read from the target language file, so `Value_Language_*Og` keys are not used. Translation completion percentages are not stored; calculate them from `en.yml` if needed.
