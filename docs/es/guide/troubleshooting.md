# Solución de problemas

## Dónde mirar

| Fichero | Contenido |
|---|---|
| `%LocalAppData%\LoopbackTranscriber\logs\app.log` | Registro de la aplicación y de sus procesos auxiliares. Cada línea lleva fecha, nivel (`INFO`, `WARN`, `ERROR`) y PID. Al pasar de 1 MB se renombra a `app.log.1` y empieza uno nuevo. |
| `%LocalAppData%\LoopbackTranscriber\live-metrics.log` | Tiempos de la traducción en vivo, fragmento a fragmento, y un resumen `SUMMARY` al acabar cada sesión. Se vacía al pasar de 512 KB. |

Cuando la aplicación se cierra por un error inesperado, el mensaje indica la ruta del registro.

Whisper y el modelo de resumen se ejecutan en **procesos auxiliares**: el mismo `LoopbackTranscriber.exe` lanzado con `--worker <tipo>`. Para identificarlos en el Administrador de tareas, abre la pestaña *Detalles* y añade la columna *Línea de comandos*. Los tipos son `whisper`, `summary`, `whisper-live` y `translate-live`.

## Problemas frecuentes

### «No se detectó ningún texto en N s de audio (posible fallo del modelo o de la GPU)»

La grabación dura 20 segundos o más y Whisper no devolvió texto. El audio se conserva como **grabación sin procesar**.

1. Pulsa **Reintentar**.
2. Si se repite, actualiza el controlador de la GPU. Whisper usa Vulkan si está disponible.
3. Comprueba en el registro qué pista se usó. Busca `falling back to device track` y `ProcessLoopbackRecorder: packets=…, peak=…`. Un `peak` de 0 indica que la aplicación no reprodujo audio.

En grabaciones de menos de 20 segundos, una transcripción vacía no se considera un error: no se guarda nada y el audio se descarta.

### La pista de la aplicación sale en silencio

Algunas aplicaciones reproducen el audio de las llamadas desde un proceso que no es hijo del principal. En ese caso la aplicación usa la pista de la **salida de audio**, que incluye cualquier otro sonido del equipo. Para evitarlo, añade en [Aplicaciones](applications.md) el proceso que aparece en **Procesos detectados con audio** durante la llamada.

### «El proceso auxiliar se cerró de forma inesperada (código N)»

El proceso de Whisper o del resumen terminó sin devolver resultado. El registro incluye las últimas 40 líneas de su salida de error.

- La causa más habitual es que falte **Microsoft Visual C++ Redistributable (x64)**. Instálalo desde <https://aka.ms/vs/17/release/vc_redist.x64.exe>.
- El código `2` significa que el proceso terminó porque la aplicación principal se cerró.

### «El modelo descargado (…) no coincide con la huella esperada y se ha descartado»

La descarga llegó dañada o incompleta. Vuelve a descargarlo desde **Configuración**. Si se repite, revisa si un proxy o un antivirus modifica las descargas.

### «No se pudo cargar el modelo …»

El fichero existe pero no se puede cargar. En el siguiente uso se vuelve a comprobar su SHA256: si está dañado se borra y se puede descargar otra vez.

### «No se ha grabado tu micrófono»

No se pudo abrir el micrófono predeterminado. Revisa en *Configuración de Windows → Privacidad y seguridad → Micrófono* que las aplicaciones de escritorio tengan acceso, y que haya un micrófono predeterminado.

### «No se pudo crear el archivo de recuperación…»

No se pudo escribir en la carpeta temporal (`%TEMP%\LoopbackTranscriber`). La grabación continúa, pero si la aplicación se cierra de golpe no se podrá recuperar. Revisa el espacio libre y los permisos.

### «No se pudo recuperar una grabación interrumpida…»

Al arrancar se encontró audio de una grabación interrumpida que no se pudo pasar a **grabaciones sin procesar**. El audio sigue en `%TEMP%\LoopbackTranscriber` y se vuelve a intentar en cada arranque.

### «No se pudo iniciar la grabación: …»

No se pudo empezar a capturar el audio de la aplicación. El estado muestra el motivo después de los dos puntos. Comprueba que la aplicación sigue abierta, vuelve a seleccionarla e inténtalo de nuevo. El detalle queda en `app.log`, en la línea `RecordingViewModel.StartRecordingAsync: failed to start recording`.

### «No se pudo iniciar el proceso auxiliar.»

La aplicación no pudo lanzar un proceso auxiliar o no obtuvo su respuesta. Cierra la aplicación desde la bandeja y vuelve a abrirla. Si se repite, revisa `app.log`.

### «No se pudo comprobar el modelo …» o «No se pudieron comprobar los modelos instalados…»

Al arrancar no se pudo leer un modelo para comprobar su huella, por ejemplo porque otro programa lo tiene abierto o faltan permisos. El modelo no se borra y se vuelve a comprobar antes de usarlo.

### «No se pudo generar el resumen: …» o «No se pudo volver a transcribir: …»

El motivo aparece después de los dos puntos y suele ser uno de los errores de esta página, como un fallo del proceso auxiliar o del modelo. «*No se encontró el audio de esta grabación.*» significa que el `.wav` de la grabación se ha borrado o movido.

### «No se pudo leer la transcripción…», «No se pudo eliminar la transcripción…» o «No se pudo eliminar la grabación…»

El fichero o la carpeta están abiertos en otro programa o no tienes permisos sobre ellos. Ciérralos e inténtalo de nuevo. Si la búsqueda falla por el mismo motivo, la lista muestra «*No se ha podido completar la búsqueda*».

### No aparece una transcripción en el historial

Si una carpeta de grabación no se puede leer, se avisa con «*No se pudo leer una transcripción y no aparece en el historial*», con su ruta. Una carpeta solo aparece en el historial si contiene un `.md`, un `.txt` o un `.wav` con su mismo nombre.

## Empezar de cero

1. Sal de la aplicación desde la bandeja.
2. Borra `%LocalAppData%\LoopbackTranscriber\settings.json`. Para empezar totalmente de cero, borra la carpeta `%LocalAppData%\LoopbackTranscriber` entera: se perderán las transcripciones y los modelos.
3. Al desinstalar, el desinstalador ofrece borrar esa carpeta y `%TEMP%\LoopbackTranscriber`.
