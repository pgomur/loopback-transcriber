# Localización

La interfaz está en **español, inglés, portugués, alemán y ruso**. Todos los textos están en una sola clase, `Services/Strings.cs`. Una comprobación en tiempo de compilación garantiza que ninguna clave falta ni sobra.

## `Strings`

Cada clave guarda un texto por idioma. Por ejemplo, `RecordStart` es «Iniciar grabación» en español y «Start recording» en inglés, y tiene también su versión en portugués, alemán y ruso.

| Miembro | Uso |
|---|---|
| `Strings.Instance` | Instancia única. Implementa `INotifyPropertyChanged`. |
| `Get(key)` / `Format(key, args)` | Texto en el idioma actual. |
| `GetIn(lang, key)` / `FormatIn(lang, key, args)` | Texto en un idioma concreto. Lo usan los mensajes del registro (en inglés) y las etiquetas del resumen. |
| `this[key]` | Para XAML: `{Binding Path=[RecordStart], Source={x:Static services:Strings.Instance}}`. |
| `SetLanguage(lang)` | Cambia el idioma, notifica `Item[]` (WPF refresca todos los *bindings*) y lanza `LanguageChanged`. |

Si a un idioma le falta una clave, se usa el inglés. Si la clave no existe, se devuelve la propia clave. La comprobación de compilación impide que ninguna de las dos cosas llegue a una versión publicada.

Cambiar el idioma en **Configuración** se aplica en el momento. `MainViewModel` reenvía `LanguageChanged` al visor y a la grabación para que reconstruyan los textos que no vienen de un *binding*, como el estado y las etiquetas de hablante.

## Qué no se traduce al guardar

Para que los ficheros no dependan del idioma de quien grabó:

- **Etiquetas de hablante** en el `.md`: siempre `Participantes`, `Tu` y `<nombre> (Tú)` (`SpeakerLabels`). El visor las traduce al mostrarlas.
- **Nombre de aplicación por defecto**: se guarda `Meeting` y se muestra con `DefaultAppName` («Reunión»). Se acepta también el antiguo `reunion`.
- **Errores**: se guardan con su clave, no con el texto. Ver [Fiabilidad](reliability.md#errores-con-significado-para-el-usuario).

En cambio, el **resumen** se escribe en el idioma de la interfaz en el momento de generarlo, con las etiquetas de sección ya traducidas. Al mostrarlo, el visor sustituye los títulos de sección por los del idioma actual (`SummaryFormatter`).

## Comprobación en compilación (`Build/LocalizationCheck.targets`)

Una tarea MSBuild en línea (`RoslynCodeTaskFactory`) se ejecuta antes de `CoreCompile`. Lee `Strings.cs`, todos los `.cs` del proyecto y todos los XAML (`Page` y `ApplicationDefinition`).

| Código | Nivel | Qué detecta |
|---|---|---|
| `LT0000` | error | No se encontró ninguna clave: el formato de `Strings.cs` ha cambiado y la tarea no sabe leerlo. |
| `LT0001` | error | Se usa una clave que no existe. |
| `LT0002` | error | A una clave le falta un idioma o lo tiene vacío. |
| `LT0003` | aviso | Una clave no se usa en ningún sitio. |
| `LT0004` | error | Clave duplicada. |

Se reconocen como usos de una clave:

- `Strings.Instance.Get("X")`, `Format("X", …)`, `GetIn(lang, "X")`, `FormatIn(lang, "X", …)` y `Strings.Instance["X"]`;
- `UserMessage.FromKey("X", …)` y `UserError.<método>("X", …)` o `UserError.<método>(inner, "X", …)`;
- en XAML, `Binding [X] … Source={x:Static …:Strings.Instance}`;
- cualquier literal que empiece por un prefijo de `KeyLiteralPrefixes`: hoy `SummaryLabel`, porque esas claves se construyen en un `switch`.

Para no marcar como «no usada» una clave que se pasa en una variable, cuenta como usada cualquier literal de C# idéntico a una clave.

La tarea se omite en el proyecto temporal `_wpftmp` que WPF genera al compilar XAML. También se puede desactivar con `-p:SkipLocalizationCheck=true`.

## Añadir un idioma

1. Añade la traducción `["xx"] = "…"` a **todas** las claves de `Strings.cs`. La comprobación avisa de cada una que falte.
2. Añade `xx` a `Languages="es;en;pt;de;ru"` en `LocalizationCheck.targets`.
3. Añádelo a `AvailableUiLanguages` (`ConfigViewModel`), a `AppSettings.DetectSystemLanguage`, a `LiveTranslationPreferences.SupportedLanguages`, a la lista de idiomas y a `LanguageNameFor` (nombre en inglés para el traductor) de `LiveTranslationViewModel`, y a la lista de idiomas y los textos de ejemplo (`PreviewSamples`) de `LiveTranslationSettingsViewModel`.
4. Añade su nombre a `SummarizationService.GetSummaryLanguageName`, que da el nombre del idioma para el *prompt*.
5. Añade el idioma a `[Languages]` del instalador si Inno Setup tiene traducción.
