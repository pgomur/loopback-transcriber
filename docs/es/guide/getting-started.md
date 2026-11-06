# Primeros pasos

## Instalación

1. Ejecuta `LoopbackTranscriberSetup-<versión>.exe`. El asistente está en español, inglés, portugués, alemán y ruso.
2. Si falta **Microsoft Visual C++ Redistributable (x64)**, el instalador lo descarga e instala, salvo que ya venga incluido en el propio instalador. Lo necesitan Whisper y el modelo de resumen. Si no se puede descargar, la instalación continúa y te indica de dónde instalarlo a mano.
3. Si el instalador de Visual C++ pide reiniciar, el asistente te lo propone al terminar.

La aplicación se instala en `Archivos de programa\LoopbackTranscriber`. Tus datos se guardan en `%LocalAppData%\LoopbackTranscriber`: consulta [Datos en disco](../architecture/data-on-disk.md).

> [!NOTE]
> Solo puede haber una instancia abierta. Si vuelves a abrir la aplicación mientras ya está en marcha (por ejemplo, minimizada en la bandeja), se muestra la ventana de la que ya existe.

## Primer arranque: descarga de modelos

Al arrancar, si falta alguno de los dos modelos (la primera vez, por ejemplo), se muestra la pantalla **Antes de empezar**:

| Modelo | Para qué | Tamaño |
|---|---|---|
| Whisper large-v3-turbo (GGML, q5_1) | Convertir el audio en texto y la transcripción en vivo | ~624 MB |
| Qwen3-4B (GGUF, Q4_K_M) | Resúmenes y traducción en vivo | ~2,5 GB |

1. Pulsa **Descargar modelos**. Los dos modelos se descargan a la vez. Cada descarga muestra su progreso y se puede **Pausar**, **Reanudar** o **Cancelar**. Una descarga en pausa continúa desde donde se quedó; **Cancelar** borra lo descargado.
2. Al terminar, se comprueba la huella SHA256 de cada fichero. Si no coincide, el fichero se descarta y tendrás que volver a descargarlo.
3. **Continuar** solo se activa cuando los dos están listos. Al pulsarlo se abre **Configuración** para que revises tu nombre y los idiomas.

Los modelos se guardan en `%LocalAppData%\LoopbackTranscriber\models`. Puedes cambiar esa carpeta en [Configuración](settings.md).

> [!TIP]
> En cada arranque, la aplicación comprueba en segundo plano que los modelos instalados no estén dañados. Si alguno lo está, lo borra y vuelve a mostrar esta pantalla para descargarlo de nuevo, salvo que haya una grabación en curso o procesándose. Los detalles están en [Modelos e integridad](../architecture/models.md).

## Ajustes recomendados antes de grabar

En **Configuración**:

- **Tu nombre**: aparece en las transcripciones en lugar de «Tú». Por defecto es el nombre de pila de tu cuenta de Windows.
- **Idioma de las reuniones**: el idioma en que se habla. Si no lo eliges, se usa el idioma de la aplicación. Si las reuniones cambian de idioma, elige **Detectar automáticamente**.
- **Idioma de la aplicación**: el de la interfaz y el de los resúmenes.

## Consentimiento

Graba y transcribe solo con el consentimiento de las personas que participan. Eres responsable de informarlas y de cumplir la ley de tu país.
