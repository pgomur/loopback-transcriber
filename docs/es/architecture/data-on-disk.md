# Datos en disco

## Ubicaciones

| Qué | Ruta | Notas |
|---|---|---|
| Ajustes | `%LocalAppData%\LoopbackTranscriber\settings.json` | Escritura atómica. Un fichero dañado se aparta como `settings.json.corrupt-<fecha>`. |
| Modelos | `%LocalAppData%\LoopbackTranscriber\models\` | `whisper\ggml-turbo.bin` y `llm\Qwen3-4B-Q4_K_M.gguf`, cada uno con su `.verified.json`. Configurable. |
| Transcripciones | `%LocalAppData%\LoopbackTranscriber\transcripts\` | Una carpeta por aplicación y una subcarpeta por grabación. Configurable. |
| Grabaciones sin procesar | `%LocalAppData%\LoopbackTranscriber\failed\` | Ver [Fiabilidad](reliability.md). |
| Registro | `%LocalAppData%\LoopbackTranscriber\logs\app.log` | Rota a `app.log.1` al pasar de 1 MB. |
| Métricas en vivo | `%LocalAppData%\LoopbackTranscriber\live-metrics.log` | Se vacía al pasar de 512 KB. |
| Grabación en curso | `%TEMP%\LoopbackTranscriber\recording_<fecha>*.wav` y `.session.json` | Se borran al procesar. Si quedan, se recuperan al arrancar. |
| Ficheros para Whisper | `%TEMP%\LoopbackTranscriber\*.16k.wav` | Se borran al terminar cada procesado. Los que queden se borran al recuperar su grabación huérfana o, si tienen más de 7 días, al terminar la siguiente grabación (procesada o conservada sin procesar). |
| Trabajos de los procesos auxiliares | `%TEMP%\LoopbackTranscriber\worker\` | `<guid>.job.json`, `<guid>.result.json` y `<guid>.live.json`. Se borran al terminar. |
| Re-transcripciones | `%TEMP%\LoopbackTranscriber\retranscribe\<guid>\` | Se borra al terminar. |

## Carpeta de transcripciones

Cada grabación es una carpeta `transcripts\<App>\<App>_<yyyyMMdd_HHmmss>\`. Dentro, `<base>` es el nombre de esa carpeta:

| Fichero | Contenido | Cuándo existe |
|---|---|---|
| `<base>.md` | La transcripción, en Markdown. | Siempre. |
| `<base>.wav` | Copia de la pista principal. | Siempre. |
| `<base>_mic.wav` | Copia de la pista del micrófono. | Si se grabó el micrófono. |
| `<base>.recording.json` | Desfase del micrófono. | Si hay `_mic.wav`. |
| `<base>.summary.json` | El resumen. | Si se generó. |

`TranscriptHistoryService.LoadFromDisk` recorre las carpetas de primer nivel y las de segundo nivel. Una carpeta es una grabación si contiene `<nombre-de-la-carpeta>.md`, `.txt` o `.wav`. Del nombre (`<App>_<yyyyMMdd>_<HHmmss>`) salen la aplicación y la fecha. Si el nombre no sigue ese patrón, se usa el nombre entero como aplicación y la fecha de modificación de la carpeta. Así se leen también las grabaciones de versiones anteriores, guardadas directamente en `transcripts\<base>\` o en `.txt`.

`TranscriptsFolderWatcher` vigila la carpeta y sus subcarpetas: la creación, el borrado y el cambio de nombre de ficheros y carpetas, no los cambios de contenido. Recarga el historial 500 ms después del último cambio. Si el vigilante falla, se vuelve a crear al recargar.

### `<base>.md`

| Parte | Formato | Ejemplo |
|---|---|---|
| Cabecera | `# <App> - dd/MM/yyyy HH:mm`, en la primera línea | `# Teams - 18/10/2026 09:30` |
| Párrafos | `<etiqueta>: <texto>`, uno por intervención | `Participantes: Buenos días a todos.` · `pgomur (Tú): La integración está terminada.` |

- El visor no muestra la cabecera como parte de la conversación.
- Los párrafos van separados por una línea en blanco.
- La etiqueta `<hablante>: ` solo aparece si se grabó el micrófono. Ver [Composición](processing.md).
- El visor lo convierte en mensajes con `TranscriptMessageParser`: quita la cabecera, separa los párrafos por las líneas en blanco y reconoce la etiqueta con el patrón de `SpeakerLabels`. Los mensajes con tu etiqueta se alinean a la derecha.

### `<base>.recording.json`

| Clave | Tipo | Significado |
|---|---|---|
| `MicStartOffsetTicks` | entero | Desfase del micrófono respecto a la pista principal, en *ticks* de .NET (100 ns). Por ejemplo, `1250000` son 0,125 s. |

Solo existe si hay `_mic.wav`. Lo usa la re-transcripción.

### `<base>.summary.json`

| Clave | Tipo | Significado |
|---|---|---|
| `Title` | texto | Título del resumen. Sustituye al nombre de la aplicación en el historial. |
| `Summary` | texto | Las secciones, cada una con su etiqueta en el idioma en que se generó (`Propósito:`, `Puntos clave:`, `Objetivos:`…) y sus puntos en líneas que empiezan por `- `. |

## Ficheros temporales de una grabación

| Fichero | Contenido |
|---|---|
| `recording_<fecha>.wav` | Pista de la aplicación (PCM 16 bits, 44,1 kHz, estéreo). |
| `recording_<fecha>_console.wav`, `_communications.wav` | Pistas de las salidas de audio predeterminadas (general y de comunicaciones), en su formato nativo. |
| `recording_<fecha>_mic.wav` | Micrófono, en su formato nativo. |
| `recording_<fecha>.session.json` | `AppName`, `RecordedAt`, `ProcessStartedUtc`, `DeviceStartedUtc`, `MicStartedUtc`. Permite la recuperación. |

Las versiones anteriores usaban el prefijo `grabacion_`, que se sigue reconociendo.
