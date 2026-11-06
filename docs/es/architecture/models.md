# Modelos e integridad

## Los dos modelos

| | Transcripción | Resumen y traducción |
|---|---|---|
| Modelo | Whisper large-v3-turbo, GGML q5_1 | Qwen3-4B, GGUF Q4_K_M |
| Origen | `huggingface.co/sandrohanea/whisper.net`, carpeta `q5_1` | `huggingface.co/Qwen/Qwen3-4B-GGUF` |
| Revisión fijada | `79dea24237e62d8a6e20a978ba415446d19712b3` | `bc640142c66e1fdd12af0bd68f40445458f3869b` |
| SHA256 | `05265418cae607e28c62db4095612e2c1967885d87cbda5a104834414b563ac8` | `7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5` |
| Dónde se fija | `WhisperTranscriptionService.ModelRevision` y `PinnedSha256` | `SummarizationService.ModelRevision` y `ModelSha256` |
| Fichero local | `<ModelsFolder>\whisper\ggml-turbo.bin` | `<ModelsFolder>\llm\Qwen3-4B-Q4_K_M.gguf` |

Las URL apuntan a un **commit concreto** (`…/resolve/<revisión>/…`), no a `main`. Si se actualiza el repositorio de origen, la aplicación sigue descargando exactamente el mismo fichero.

## Descarga

`EnsureModelDownloadedAsync` funciona igual en los dos servicios:

1. Si el fichero ya existe y pasa la verificación (ver abajo), termina.
2. Descarga a `<destino>.download` en bloques de 1 MB. Si ya existe un `.download` de un intento anterior, **reanuda** con una cabecera `Range`; si el servidor no responde con 206, empieza desde cero. El `.download` solo se conserva si la descarga se cancela (es lo que hace *Pausar*) o si la aplicación se cierra a medias. Un error de red o de disco lo borra.
3. Informa del progreso cada 200 ms como mucho.
4. Si los bytes recibidos no coinciden con `Content-Length`, lanza `ErrorDownloadIncomplete`.
5. Calcula el **SHA256** del `.download`. Si no coincide, lo borra y lanza `ErrorModelHashMismatch`.
6. Mueve el `.download` al destino y escribe el marcador de verificación.

En Whisper, si la descarga falla, se reintenta una vez antes de lanzar `ErrorModelDownloadFailed`. Un modelo **sin hash fijado** nunca se descarga: se lanza `ErrorModelNotPinned`.

## Verificación sin recalcular el hash cada vez

Calcular el SHA256 de 2,5 GB tarda. `ModelIntegrity` guarda junto a cada modelo un marcador `<modelo>.verified.json`:

| Campo | Qué guarda |
|---|---|
| `Sha256` | El hash con el que se comprobó el fichero. |
| `Length` | El tamaño del fichero en bytes al comprobarlo. |
| `LastWriteTimeUtcTicks` | La fecha de modificación del fichero (UTC, en *ticks*) al comprobarlo. |

`EnsureVerifiedAsync(ruta, hashEsperado)`:

1. Si el marcador coincide con el hash esperado, el tamaño y la fecha de modificación actuales, el modelo **se da por bueno sin leerlo**.
2. Si no, recalcula el SHA256:
   - si coincide, escribe el marcador y lo da por bueno;
   - si no coincide, **borra el modelo** y su marcador y devuelve `false` para que se vuelva a descargar.
3. Las verificaciones del mismo fichero se serializan con un semáforo por ruta.

Si un modelo existe pero **no se puede cargar** (`WhisperModelLoadException` o `LoadWeightsFailedException`), `ModelIntegrity.LoadFailed` borra el marcador y lanza `ErrorModelLoadFailed`. En el siguiente uso se recalcula el hash: si el fichero está dañado se borra y se descarga, y si no, el problema está en otro sitio (GPU, memoria).

## Verificación al arrancar

`MainViewModel.Start` lanza en segundo plano `IModelVerifier.VerifyInstalledModelsAsync`:

- Comprueba los dos modelos instalados con `EnsureVerifiedAsync`. Normalmente basta con leer el marcador.
- Si alguno estaba dañado y se borró, vuelve a la pantalla de primera ejecución. No lo hace si hay una grabación en curso o procesándose.
- Si no se puede leer un modelo (bloqueado, sin permisos), avisa con `NoticeModelVerificationFailed` y lo deja: se volverá a comprobar antes de usarlo.

## Actualizar un modelo

1. En la página del fichero en Hugging Face, copia el **commit** y el **SHA256** del fichero.
2. Cambia la revisión y el hash **a la vez**:
   - Whisper: `ModelRevision` y la entrada `GgmlType.LargeV3Turbo` de `PinnedSha256` en `WhisperTranscriptionService.Download.cs`.
   - Qwen3: `ModelRevision` y `ModelSha256` en `SummarizationService.Download.cs`. Si cambia el nombre del fichero, también `ModelDownloadUrl`, `ModelName` y `AppSettings.SummaryModelFileName`.
3. Actualiza los tamaños de los textos `FirstRun*ModelSize` y `Config*ModelName` en `Strings.cs`.
4. Los usuarios que ya tengan el modelo anterior verán que su marcador no coincide con el nuevo hash. El modelo se recalculará, se borrará y se descargará el nuevo.

Para admitir otro tamaño de Whisper (`small`, `medium`…), basta con añadir su hash a `PinnedSha256`. `ParseModelSize` ya reconoce los nombres.
