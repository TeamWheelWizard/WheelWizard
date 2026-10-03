# Settings and localization

## Compatibility

Existing `config.json` property names and JSON value types are unchanged. Unknown properties survive a save. There is no migration or schema version bump. Older Wheel Wizard versions can read files saved by this implementation.

Loading settings does not rewrite the files. Invalid individual values use defaults. An unreadable or malformed application settings file is preserved and further saves report failure. Atomic writes retain the previous file as `.bak`. Dolphin and WiiCompiled saves update the selected key while preserving other keys. WiiCompiled must create its own `Config.toml` before it can be edited.

Atomicity applies to one file replacement, not a group of settings across files. The recommended-settings action may partially succeed if a later write fails; it reports that failure. Simultaneous external writes during a file replacement are not coordinated across processes.

## Typed settings

`Setting<T>` gives application callers a typed value and setter:

```csharp
bool enabled = settings.ENABLE_ANIMATIONS.Get();
bool saved = settings.Set(settings.ENABLE_ANIMATIONS, false);
```

A string passed to that boolean setting is a compiler error. Runtime parsing and validation still exist at JSON/INI/TOML boundaries because those files are external input. Domain validation, such as checking paths, also remains necessary. Failed writes return `false`, retain the previous setting value, and expose `SaveError`; successful writes publish change notifications. Use the manager when editing paths so Dolphin settings reload for the selected profile.

## Translation calls

`ILocalizationService` owns the active language and emits `LanguageChanged`. The global `LocalizationProvider` forwards to the service registered by settings startup, keeping the global functions and XAML markup available.

```csharp
t("action.cancel");
t("items", count: 1);
t("items", 2, "Alex"); // {$1} is the count, {$2} is Alex
t("fr.items", count: 2); // explicit language
```

New plural messages use `zero`, `one`, `two`, `few`, `many`, and `other` category suffixes. These names are grammatical categories: `one` does not always mean the number 1. Include `other` for every category-based message. `PluralRules` defines CLDR 48 cardinal rules for the languages in the app's language selector. Unknown languages use `other`. Decimal scale is significant: English `1m` uses `one`, while `1.0m` uses `other`.

Lookup tries the selected category, `other`, and a plain scalar in the requested language, then repeats in English using **English's** category rule. A missing message returns its key. A numeric first argument selects plural lookup; a plain scalar still supports the existing numbered placeholders.

The imported translation files have not been converted. Until the source sheet changes a message, use:

```csharp
t_legacy("time.days.n", 1);
```

This retains the exact numeric suffix lookup (`.1`, `.2`, etc.) followed by `.n`. `tTime()` continues to use it. Migrate the call and sheet entry together; do not rename imported keys locally.

XAML `{loc:T key}` is an observable binding. Language changes update existing controls on the UI thread. The persistent layout and active Wheel Wizard settings page also refresh their code-assigned labels. New code-assigned translated text needs its own refresh when it remains visible across language changes; use XAML bindings where possible. No window recreation is required.

## Validation

Run from the repository root:

```shell
dotnet test WheelWizard.sln
```

`TranslationCatalogTests` checks imported YAML structure, duplicate flattened keys/languages, English fallback availability, selector languages, matching numbered placeholders, and `other` entries for category messages. Missing translations are reported in test output and continue to fall back to English. Empty sheet stubs remain unavailable. Plural behavior uses test-only embedded YAML; production imports stay untouched.

The stack merges bottom-up: settings persistence, typed settings, plural translations, live localization, then catalog validation and this guide. Each layer builds and passes its checks without requiring later layers.
