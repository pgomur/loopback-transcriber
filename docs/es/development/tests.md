# Tests

`LoopbackTranscriber.Tests` usa **xUnit 2.9.3** y se compila para `net10.0-windows` con WPF, porque los ViewModels dependen de tipos de WPF como `DispatcherTimer`. Los tests se ejecutan **sin ventana, sin dispositivos de audio, sin modelos y sin procesos auxiliares**: todo lo externo se sustituye por dobles.

## Ejecutar

| Comando | Ejecuta |
|---|---|
| `dotnet test LoopbackTranscriber.slnx -c Release` | Todos los tests. |
| `dotnet test LoopbackTranscriber.slnx --filter Category=Unit` | Solo los unitarios. |
| `dotnet test LoopbackTranscriber.slnx --filter Category=Integration` | Solo los de integración. |
| `dotnet test LoopbackTranscriber.slnx --filter "FullyQualifiedName~RecordingWorkflow"` | Los de una clase, por su nombre. |

En Visual Studio: *Prueba → Explorador de pruebas → Ejecutar todas*. Para agrupar por categoría, usa *Agrupar por → Rasgos*.

## Organización

| Carpeta o fichero | Contenido |
|---|---|
| `GlobalUsings.cs` | Espacios de nombres comunes y el alias `TranscriptSegment`. |
| `Infrastructure\` | Utilidades compartidas por los tests. |
| `Fakes\` | Dobles de las interfaces de la aplicación. |
| `Models\` | Un fichero por tipo de `Models\`. |
| `Services\` | Un fichero por servicio. |
| `ViewModels\` | Un fichero por ViewModel. |

Cada clase de test se llama como la clase que prueba más `Tests`. Cada test tiene la forma `Situacion_ResultadoEsperado`, por ejemplo `WhileRecording_No_ExitsAtOnce_KeepingTheAudioToProcessLater`.

### Categorías

Cada clase lleva un rasgo `[Trait(TestCategory.Name, …)]`:

| Categoría | Significado |
|---|---|
| `Unit` | Lógica pura: sin disco, sin reloj y sin hilos. Tarda milisegundos. |
| `Integration` | Usa una carpeta temporal, ficheros WAV reales o tiempos reales. Sigue siendo rápida y aislada. |

## Infraestructura

| Tipo | Para qué |
|---|---|
| `TestEnvironment` | `[ModuleInitializer]` que se ejecuta antes de cualquier test. Crea `%TEMP%\LoopbackTranscriber.Tests\<pid>`, redirige `AppLog.LogPath` ahí para no escribir en el registro real y fija el idioma en español. |
| `TempDirectory` | Carpeta temporal propia de cada test. Se borra al liberarla. |
| `TestSettings` | `AppSettings` con todas las carpetas dentro de la temporal. `CreateModelFiles` crea modelos falsos de 1 byte para que la aplicación crea que están instalados. |
| `WavFile` | Escribe y lee WAV de prueba: tonos, silencio, PCM o *float*, cabeceras rotas a propósito, duración y formato. |
| `Eventually.Until(condición, motivo)` | Espera sin bloquear, comprobando la condición cada 10 ms, hasta 5 s por defecto. Si no se cumple, falla con el motivo. |
| `CollectingProgress<T>` | `IProgress<T>` síncrono que guarda los valores. |
| `InterfaceLanguage.Use(lang)` | Cambia el idioma de la interfaz y lo restaura al liberarse. |
| `InterfaceLanguageTestGroup` | Colección de xUnit **sin paralelismo**. El idioma es estado global, así que los tests que lo cambian van en ella. |
| `RecordingRig` | Monta el flujo de grabación completo con dobles: `CaptureCoordinator`, `RecordingWorkflow`, `RecordingProcessor`, `RecordingViewModel`, el almacén de fallidas y Teams como aplicación. Incluye ayudas como `StartRecordingAsync`, `WhisperSays` y `LeaveOrphan`. |
| `ShellRig` | `MainViewModel` con todos sus hijos, sobre un `RecordingRig`. |

## Dobles

| Doble | Sustituye a | Qué permite |
|---|---|---|
| `FakeProcess` | `IProcessCapture` | Escribir un WAV de prueba, retrasar el arranque (`StartGate`), contar arranques y fijar el pico. |
| `FakeDevice` | `IDeviceCapture` | Devolver pistas del dispositivo con el pico que se quiera. |
| `FakeMic` | `IMicCapture` | Simular un micrófono que arranca o no (`StartSucceeds`). |
| `FakeWhisper` | `IWhisperTranscriber` | Devolver segmentos por nombre de fichero, un idioma detectado, retener la transcripción (`Gate`) y registrar las peticiones. |
| `FakeAudio` | `IAudioPreparer` | Duración, mapas de actividad y de nivel, y fallos de la puerta de ruido. |
| `FakeSettingsStore` | `ISettingsStore` | Ajustes en memoria. Cada lectura devuelve una **copia**, como el servicio real. Cuenta las actualizaciones. |
| `FakeDialogs` | `IDialogService` | Respuestas fijas a Sí/No y Sí/No/Cancelar. Registra errores e información. |
| `FakeTargetWatcher` | `ITargetWatcher` | Marcar aplicaciones como abiertas o cerradas (`SetRunning`). |
| `FakeCustomApps` | `ICustomAppsService` | Añadir y quitar aplicaciones. |
| `FakeSummarizer` | `ISummarizer` | Resumen fijo y contador de llamadas. |
| `FakeLiveHost` | `IRecordingHost` | Grabación simulada para la ventana flotante y la bandeja: retrasar o hacer fallar el arranque y reaccionar a la parada (`OnStop`). |
| `FakePageFactory` | `IPageFactory` | Contar las páginas creadas. |
| `FakeModelVerifier` | `IModelVerifier` | Resultado o excepción de la verificación de modelos. |

## Pautas

- **Ningún test toca el estado real del usuario**: ni `settings.json`, ni `%LocalAppData%`, ni el registro, ni procesos o dispositivos reales.
- Los servicios que guardan en disco reciben su ruta por el constructor, como `SettingsService(notices, settingsPath)` o `FailedRecordingStore(rootFolder)`. Los tests les pasan una ruta dentro de su `TempDirectory`.
- Para esperar algo asíncrono, usa `Eventually.Until`, no `Task.Delay` con un tiempo fijo. Para controlar cuándo termina una operación, usa un `TaskCompletionSource` (los `Gate` de los dobles).
- Los mensajes entre ViewModels se prueban con un `WeakReferenceMessenger` propio de cada test, no con `WeakReferenceMessenger.Default`.
- Los textos esperados se obtienen con `Strings.Instance.Get(...)` o `Format(...)` en lugar de escribirlos a mano, salvo cuando lo que se prueba es la traducción.
- No uses tu nombre real en los datos de prueba: los tests usan `pgomur`.
