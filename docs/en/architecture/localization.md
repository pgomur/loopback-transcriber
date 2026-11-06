# Localization

The interface is available in **Spanish, English, Portuguese, German and Russian**. All texts live in a single class, `Services/Strings.cs`. A build-time check guarantees that no key is missing and none is left over.

## `Strings`

Each key holds one text per language. For example, `RecordStart` is "Iniciar grabación" in Spanish and "Start recording" in English, and it also has its Portuguese, German and Russian versions.

| Member | Used for |
|---|---|
| `Strings.Instance` | Single instance. Implements `INotifyPropertyChanged`. |
| `Get(key)` / `Format(key, args)` | Text in the current language. |
| `GetIn(lang, key)` / `FormatIn(lang, key, args)` | Text in a given language. Used by log messages (in English) and by the summary labels. |
| `this[key]` | For XAML: `{Binding Path=[RecordStart], Source={x:Static services:Strings.Instance}}`. |
| `SetLanguage(lang)` | Changes the language, raises `Item[]` (WPF refreshes every binding) and fires `LanguageChanged`. |

If a language lacks a key, English is used. If the key does not exist, the key itself is returned. The build check stops either case from reaching a published version.

Changing the language in **Settings** takes effect immediately. `MainViewModel` forwards `LanguageChanged` to the viewer and the recording so they rebuild the texts that do not come from a binding, such as the status and the speaker labels.

## What is not translated when saving

So that files do not depend on the language of whoever recorded them:

- **Speaker labels** in the `.md`: always `Participantes`, `Tu` and `<name> (Tú)` (`SpeakerLabels`). The viewer translates them when displaying them.
- **Default application name**: `Meeting` is stored and shown through `DefaultAppName` ("Meeting", "Reunión"…). The old `reunion` is accepted too.
- **Errors**: stored with their key, not their text. See [Reliability](reliability.md#errors-that-mean-something-to-the-user).

The **summary**, on the other hand, is written in the interface language at the time it is generated, with the section labels already translated. When displaying it, the viewer replaces the section titles with those of the current language (`SummaryFormatter`).

## Build-time check (`Build/LocalizationCheck.targets`)

An inline MSBuild task (`RoslynCodeTaskFactory`) runs before `CoreCompile`. It reads `Strings.cs`, every `.cs` in the project and every XAML file (`Page` and `ApplicationDefinition`).

| Code | Level | What it detects |
|---|---|---|
| `LT0000` | error | No key was found: the format of `Strings.cs` has changed and the task cannot read it. |
| `LT0001` | error | A key is used that does not exist. |
| `LT0002` | error | A key is missing a language, or has it empty. |
| `LT0003` | warning | A key is not used anywhere. |
| `LT0004` | error | Duplicate key. |

These count as uses of a key:

- `Strings.Instance.Get("X")`, `Format("X", …)`, `GetIn(lang, "X")`, `FormatIn(lang, "X", …)` and `Strings.Instance["X"]`;
- `UserMessage.FromKey("X", …)` and `UserError.<method>("X", …)` or `UserError.<method>(inner, "X", …)`;
- in XAML, `Binding [X] … Source={x:Static …:Strings.Instance}`;
- any literal starting with a prefix from `KeyLiteralPrefixes`: currently `SummaryLabel`, because those keys are built in a `switch`.

So that a key passed through a variable is not flagged as unused, any C# literal identical to a key counts as a use.

The task is skipped in the temporary `_wpftmp` project that WPF generates when compiling XAML. It can also be turned off with `-p:SkipLocalizationCheck=true`.

## Adding a language

1. Add the `["xx"] = "…"` translation to **every** key in `Strings.cs`. The check reports each one that is missing.
2. Add `xx` to `Languages="es;en;pt;de;ru"` in `LocalizationCheck.targets`.
3. Add it to `AvailableUiLanguages` (`ConfigViewModel`), to `AppSettings.DetectSystemLanguage`, to `LiveTranslationPreferences.SupportedLanguages`, to the language list and `LanguageNameFor` (English name for the translator) in `LiveTranslationViewModel`, and to the language list and sample texts (`PreviewSamples`) in `LiveTranslationSettingsViewModel`.
4. Add its name to `SummarizationService.GetSummaryLanguageName`, which gives the language name for the prompt.
5. Add the language to the installer's `[Languages]` if Inno Setup has a translation for it.
