# Traducción en vivo

La traducción en vivo abre una **ventana flotante** que muestra, mientras se graba, lo que dicen los participantes y su traducción. Sirve para seguir una reunión en un idioma que no dominas.

## Abrirla

- Desde la vista de la aplicación, con el botón **Traducción en vivo**. Solo está activo si la aplicación está abierta. Si la ventana flotante ya está abierta para esa aplicación, el mismo botón la cierra.
- Desde el icono de la bandeja (con la ventana principal minimizada u oculta): clic derecho → **Traducción en vivo de X**. Con la ventana abierta, la misma entrada pasa a ser **Cerrar traducción en vivo de X**.

Solo puede haber una ventana flotante a la vez: abrirla para otra aplicación cierra la anterior. Aparece pegada al borde derecho de la pantalla, centrada en vertical.

## Usarla

1. En **Traducir a:**, elige el idioma de destino: español, inglés, portugués, alemán o ruso. La elección se recuerda.
2. Pulsa **Iniciar grabación y traducir**. Se inicia una grabación normal de esa aplicación y la ventana empieza a mostrar texto en unos segundos.
3. Pulsa **Detener grabación** para parar. La grabación se procesa como cualquier otra, la ventana muestra el progreso y la transcripción completa queda en el historial.

Si la grabación de esa aplicación se inicia por otro camino (la ventana principal o la bandeja) con la ventana flotante abierta, la traducción en vivo empieza igualmente.

Mientras alguien habla, aparece un **borrador** que se va corrigiendo. Cuando hay una pausa, la frase se fija como línea definitiva y se traduce. Las frases largas se dividen en varias líneas.

La línea de estado muestra «*Grabando · 00:00*» y, mientras se cargan los modelos, «*Cargando el modelo de voz…*» y «*cargando el traductor…*». Sin grabar, muestra «*En reposo*».

### Controles de la ventana

| Control | Qué hace |
|---|---|
| Opacidad | Hace la ventana más o menos transparente, entre el 30 % y el 100 %. Se recuerda. |
| Siempre encima | Mantiene la ventana por encima de las demás. Está activado al abrirla. |
| Abrir Loopback Transcriber | Muestra la ventana principal. |
| Cerrar | Cierra la ventana flotante. La grabación, si la hay, sigue en curso. |

### Datos técnicos

Si está activado **Mostrar datos técnicos**, una franja inferior muestra:

- **Señal**: el nivel del audio de la aplicación, o «*sin audio de la app*».
- **Último fragmento** (o **borrador**): segundos de audio, segundos de proceso y retraso desde que terminó de hablarse.
- **Traducción**: tiempo de traducción y retraso.

## Ajustes

En la barra lateral, **Traducción en vivo** abre los ajustes de la ventana flotante, con una vista previa:

| Ajuste | Por defecto |
|---|---|
| Traducir a | El último elegido; la primera vez, el idioma de la aplicación |
| Mostrar el texto original bajo la traducción | Sí |
| Tamaño del texto | 19 (de 14 a 32) |
| Opacidad de la ventana | 100 % (de 30 % a 100 %) |
| Mostrar datos técnicos (señal y tiempos) | Sí |

Los cambios se aplican al momento a la ventana flotante, si está abierta.

## Limitaciones

- **Solo se transcribe en vivo el audio de la aplicación**, no tu micrófono. Tu voz sí aparece en la transcripción final.
- La traducción usa el mismo modelo que los resúmenes (Qwen3-4B). Si no está descargado, la ventana solo muestra la transcripción e indica «*sin traducción: falta el modelo de resumen*».
- Si falta el modelo de voz, la ventana indica «*Falta el modelo de voz. Descárgalo en Configuración.*».
- Si el equipo no da abasto, se descartan primero los fragmentos más antiguos para no acumular retraso. Puede faltar alguna frase en la ventana, pero nunca en la transcripción final, que se hace después sobre el audio completo.

El funcionamiento interno se describe en [Arquitectura: traducción en vivo](../architecture/live-translation.md).
