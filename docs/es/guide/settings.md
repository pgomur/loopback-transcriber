# Configuración

## Pantalla de configuración

### General

| Ajuste | Qué hace | Por defecto |
|---|---|---|
| **Tu nombre** | Etiqueta tus intervenciones como «*Nombre (Tú)*» en lugar de «*Tú*». Hasta 20 caracteres. Se guarda 0,7 s después de dejar de escribir. Si lo dejas vacío, se usa «*Tú*». | El nombre de pila de tu cuenta de Windows o, si no lo tiene, tu usuario con la inicial en mayúscula. |
| **Idioma de la aplicación** | Idioma de la interfaz y de los resúmenes: español, inglés, portugués, alemán o ruso. Se aplica al momento. | El de Windows, si es uno de esos cinco. Si no, inglés. |
| **Idioma de las reuniones** | Idioma en que se habla, para tu micrófono y los participantes: **Detectar automáticamente** o uno de los cinco idiomas. | Sigue al idioma de la aplicación mientras no elijas otro. |
| **Interfaz traslúcida** | Aplica el efecto Mica de Windows 11 al fondo de la ventana. No se aplica en la pantalla de primera ejecución. | Desactivado |

> [!TIP]
> Fijar el idioma de las reuniones da mejores resultados que la detección automática si siempre hablas el mismo idioma. Usa **Detectar automáticamente** si cambia de una reunión a otra. Cómo se detecta se explica en [Procesado de una grabación](../architecture/processing.md#idioma).

### Modelos de IA

Dos apartados, **Transcripción** y **Resumen con IA**, muestran cada modelo, su tamaño en disco o si falta, y un botón para descargarlo: **Descargar modelo de transcripción** y **Descargar modelo de resumen**.

- Si un modelo ya está en la carpeta, se comprueba su integridad en lugar de descargarlo otra vez.
- El estado se actualiza solo si aparecen o desaparecen ficheros en la carpeta de modelos.
- Aquí las descargas no se pueden pausar. Si se interrumpen, por ejemplo al cerrar la aplicación, la siguiente descarga continúa desde donde se quedó.

### Carpetas y guardado

| Carpeta | Por defecto |
|---|---|
| Carpeta de modelos | `%LocalAppData%\LoopbackTranscriber\models` |
| Carpeta de transcripciones | `%LocalAppData%\LoopbackTranscriber\transcripts` |

**Abrir** la muestra en el Explorador y **Cambiar** elige otra.

> [!IMPORTANT]
> Cambiar una carpeta **no mueve** su contenido. Si cambias la carpeta de modelos, mueve tú los ficheros o vuelve a descargarlos. Si cambias la de transcripciones, el historial mostrará solo lo que haya en la nueva. Al desinstalar, las carpetas que hayas cambiado no se borran.

### Acerca de

El enlace al perfil de GitHub del autor, el aviso legal (consentimiento para grabar, procesamiento local y que los resúmenes pueden contener errores) y **Licencias de software de terceros**, que abre `THIRD-PARTY-NOTICES.txt`.

## El fichero de ajustes

Todos los ajustes se guardan en `%LocalAppData%\LoopbackTranscriber\settings.json`. La aplicación lo escribe sola y no hace falta editarlo. Lo describimos aquí para diagnosticar problemas y porque tres ajustes avanzados solo existen en este fichero.

| Clave | Tipo | Significado |
|---|---|---|
| `ModelSize` | texto | Modelo de Whisper. Por defecto `turbo`, el único con huella SHA256 fijada. Otro valor (`tiny`, `base`, `small`, `medium`, `large`) solo funciona si el fichero `ggml-<valor>.bin` ya está en `models\whisper`, y **no se verifica**. Si no está, se produce un error. La primera ejecución y la verificación al arrancar usan siempre `turbo`, y cualquier cambio guardado desde **Configuración** devuelve este valor a `turbo`. |
| `ModelsFolder` | texto | Carpeta de modelos. |
| `TranscriptsFolder` | texto | Carpeta de transcripciones. |
| `MeetingLanguage` | texto | `""` sigue al idioma de la aplicación, `auto` detecta el idioma, y `es`, `en`, `pt`, `de` o `ru` fijan uno. |
| `UiLanguage` | texto | `es`, `en`, `pt`, `de` o `ru`. |
| `TranslucentUi` | booleano | Interfaz traslúcida. |
| `UserName` | texto | Tu nombre en las transcripciones. En las etiquetas, los dos puntos se sustituyen por espacios. |
| `UserNameInitialized` | booleano | Ya se propuso el nombre de Windows. No se vuelve a proponer aunque borres el nombre. |
| `TrayHintShown` | booleano | Ya se mostró el aviso de «sigue en la bandeja». |
| `FirstRunSetupCompleted` | booleano | Ya se completó la descarga inicial de modelos. |
| `CustomApps` | lista | Aplicaciones añadidas: `Id` (`custom:<proceso en minúsculas>`), `DisplayName` y `ProcessName`. |
| `LiveTranslationLanguage` | texto | Idioma de destino de la traducción en vivo. |
| `LiveWindowOpacity` | número | Opacidad de la ventana flotante, de 0,3 a 1. |
| `LiveShowOriginal` | booleano | Mostrar el original bajo la traducción. |
| `LiveShowMetrics` | booleano | Mostrar la franja de datos técnicos. |
| `LiveTextSize` | número | Tamaño del texto de la ventana flotante, de 14 a 32. |
| `LiveAudioContext` | entero | **Avanzado.** Tamaño del contexto de audio de Whisper en la transcripción en vivo. `0` usa el del modelo. Valores menores reducen la latencia a costa de precisión. |
| `LiveFlashAttention` | booleano | **Avanzado.** Activa *flash attention* en Whisper en vivo. Si el modelo no carga con ella, se reintenta sin ella. |

Si editas el fichero a mano, hazlo con la aplicación cerrada. La aplicación lee el fichero solo al arrancar y, con cada cambio de ajustes, lo reescribe entero con lo que tiene en memoria, así que tus cambios se perderían.

### Qué pasa si el fichero se daña

- Si `settings.json` no es un JSON válido, se aparta como `settings.json.corrupt-<fecha>`, se usan los valores por defecto y se avisa: «*No se pudo leer la configuración y se han restablecido los valores por defecto.*».
- Si no se puede escribir (disco lleno, sin permisos, antivirus), se intenta tres veces. Los cambios se mantienen en memoria, se vuelve a intentar con el siguiente cambio y al cerrar, y se avisa una vez.

La escritura es atómica: se escribe un `settings.json.tmp` y se sustituye el original. Un corte de luz no deja el fichero a medias.
