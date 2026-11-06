# Fiabilidad

El objetivo es que **ninguna grabación se pierda**: si no se puede transcribir ahora, su audio se conserva para reintentarlo después. Esta página recoge todos los caminos por los que una grabación puede quedar a medias y qué se hace en cada uno.

## Grabaciones sin procesar (`FailedRecordingStore`)

Una grabación conservada es una carpeta en `%LocalAppData%\LoopbackTranscriber\failed\`:

El nombre de la carpeta es la fecha de la grabación y 8 caracteres aleatorios, por ejemplo `20261018_093000_1a2b3c4d`. Dentro:

| Fichero | Contenido |
|---|---|
| `manifest.json` | `AppName`, `RecordedAt`, `SourceWav`, `MicWav`, `MicStartOffsetTicks`, `Error`, `ErrorKey` y `ErrorArgs`. |
| `meeting.wav` | La pista principal. |
| `microphone.wav` | El micrófono, si lo había. |

- `TryPreserve` crea la carpeta, escribe el manifiesto y **mueve** los WAV. Si no puede mover el micrófono, se queda solo con la pista principal y avisa. Si falla la pista principal, deshace lo hecho y devuelve `null`, y los originales se quedan donde estaban.
- `List` lee los manifiestos y descarta las carpetas sin `meeting.wav`. Un manifiesto ilegible genera un aviso (`NoticeUnreadableFailedRecording`) y la carpeta no se toca.
- El error se guarda con su **clave de localización** (`ErrorKey`, `ErrorArgs`), así el motivo se muestra en el idioma actual aunque se haya cambiado desde entonces.
- **Reintentar** (`RecordingWorkflow.RetryAsync`) procesa la grabación con los ajustes actuales y, si sale bien, borra la carpeta. Si vuelve a fallar, la carpeta se queda como estaba. **Descartar** la borra después de pedir confirmación.
- `RecordingViewModel` muestra en la vista de cada aplicación las grabaciones con su `AppName` y, en todas, las que tienen el nombre por defecto (`Meeting`).

## Cada camino, cubierto

| Qué pasa | Dónde se trata | Resultado |
|---|---|---|
| Falla la transcripción (modelo, GPU, sin texto en ≥ 20 s) | `RecordingWorkflow.StopAndProcessAsync` | El audio pasa a *sin procesar* con el motivo. |
| Sin texto en una grabación de < 20 s | `RecordingProcessor.ProcessAsync` | **Única excepción**: no es un fallo. No se guarda nada y el audio se borra. |
| El usuario sale con una grabación en curso y elige *No* | `AppExitWorkflow` → `PreserveInterruptedRecordingAsync` (10 s) | *Sin procesar* (`RecordingInterruptedReason`). |
| Windows cierra sesión o se apaga | `MainWindow.OnSessionEnding` → `PreserveInterruptedRecording` (3 s) | *Sin procesar*. |
| Excepción no controlada en la interfaz | `CrashHandler` → `PreserveInterruptedRecording` (3 s) | *Sin procesar*. El mensaje lo confirma (`CrashBodyPreserved`). |
| Excepción no controlada en otro hilo | `CrashHandler` → rescate en el hilo de la interfaz (2 s para responder) | *Sin procesar* si da tiempo. |
| El proceso muere de golpe (corte de luz, administrador de tareas) | `OrphanRecordingRecovery` en el siguiente arranque | *Sin procesar* (`RecordingRecoveredReason`). |
| No se puede mover a `failed\` | `CaptureCoordinator.PreserveFailed` | Los originales se quedan en `%TEMP%` y los recupera el siguiente arranque. |
| Se cuelga el controlador de audio al parar | `ProcessLoopbackRecorder.AbandonStuckCapture` | Se cierra el WAV con lo capturado y se sigue. |

Los rescates síncronos (`PreserveInterruptedRecording`) se usan donde no se puede esperar con `await`, como el manejador de errores o `SessionEnding`. Ejecutan el trabajo en el *thread pool* y esperan con un límite de tiempo. Como nunca necesitan el hilo que llama, no pueden bloquearse mutuamente.

## Grabaciones huérfanas

Una grabación es **huérfana** si su audio está en `%TEMP%\LoopbackTranscriber` pero ninguna instancia la está grabando. `MainViewModel.Start` llama a `RecordingViewModel.RecoverOrphanRecordingsAsync`, que ejecuta `OrphanRecordingRecovery.Run`:

1. **Busca** los `recording_*.session.json` (y `grabacion_*`, el prefijo de versiones antiguas) y los WAV con nombre de grabación que no tienen `.session.json`.
2. **Omite** las que pertenecen a la instancia actual (`CaptureCoordinator._ownedBaseNames`).
3. **Aplaza** las que tienen algún WAV en uso: lo comprueba abriéndolo con `FileShare.None`. Se reintenta hasta 4 veces, cada 30 s. Si siguen en uso, quedan para el siguiente arranque.
4. Para cada una:
   - Lee la sesión. Sin `.session.json`, la aplicación es `Meeting` y la fecha se toma del nombre del fichero.
   - Ignora los WAV derivados (`.16k.wav`) y los que no tienen datos más allá de la cabecera (`WavRepair.HasAudioData`).
   - **Repara la cabecera** de cada WAV (`WavRepair.TryRepairHeader`). Si el tamaño de `data` es 0 o mayor que el fichero, escribe el tamaño real, redondeado al `blockAlign`, y corrige el de `RIFF`.
   - **Elige la pista** con el mismo criterio que una parada normal: el proceso si tiene un pico ≥ 0,01; si no, la pista de salida con más pico ≥ 0,01; si no, la del proceso aunque esté en silencio. Solo si no hay pista del proceso ni de salida, el micrófono pasa a ser la pista principal. Ver [Captura de audio](audio-capture.md).
   - Calcula el desfase del micrófono con las horas guardadas en la sesión.
   - Mueve las pistas a `failed\` y borra el resto.
   - Si no hay audio, borra todo sin avisar.
5. Las que fallan se avisan con `NoticeOrphanRecoveryFailed`. Su audio sigue en `%TEMP%` y se reintentan en el siguiente arranque.

## Ajustes

`SettingsService` guarda los ajustes en memoria como JSON: cada `Snapshot()` es una copia independiente y cada `Update(cambio)` aplica el cambio a una copia nueva bajo un *lock*.

- **Escritura atómica**: `settings.json.tmp` y después `File.Replace`, o `File.Move` la primera vez.
- **Sin escrituras inútiles**: si el JSON no cambia, no se escribe.
- **Reintentos**: 3 intentos, con 100 ms × intento de espera. Si fallan todos, los cambios quedan pendientes en memoria, se avisa una vez (`NoticeSettingsNotSaved`) y se reintenta en el siguiente cambio y en `App.OnExit` (`FlushPendingWrite`).
- **Fichero dañado**: se renombra a `settings.json.corrupt-<yyyyMMdd-HHmmss>`, se usan los valores por defecto y se avisa (`NoticeSettingsReset`).

## Errores con significado para el usuario

`UserError` crea excepciones normales (`InvalidOperationException`, `InvalidDataException`, `IOException`) con su **clave de localización** y argumentos en `Exception.Data`. El `Message` va en inglés para el registro.

- `UserMessage.From(ex)` recupera la clave, y `Localize()` la traduce al idioma actual cuando se muestra.
- Una excepción sin clave se muestra como `ErrorUnexpected` con su mensaje.
- Las claves viajan intactas a través de los procesos auxiliares (`ErrorKey` y `ErrorArgs`) y de las grabaciones sin procesar (`manifest.json`).

## Fallos esperados y fallos de programación

`ExpectedFailures` agrupa las excepciones que se consideran fallos del entorno:

| Predicado | Excepciones |
|---|---|
| `IsFile` | `IOException`, `UnauthorizedAccessException` |
| `IsAudioFile` | las anteriores, `FormatException` e `InvalidDataException` |
| `IsDeviceOrFile` | `COMException`, `InvalidComObjectException`, `IOException`, `UnauthorizedAccessException` |

Los `catch` del código filtran con estos predicados (`catch (Exception ex) when (ExpectedFailures.IsFile(ex))`). Un fallo de programación, como un `NullReferenceException`, no se oculta: llega al `CrashHandler` y queda en el registro.
