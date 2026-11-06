# Grabar una reunión

## Elegir la aplicación

En la barra lateral aparecen las aplicaciones que se vigilan. **Teams** viene de serie. Puedes añadir cualquier otra que reproduzca audio (Zoom, el navegador para Meet, Discord…) desde [Aplicaciones](applications.md).

La lista se actualiza cada 1,5 segundos. Una aplicación solo se puede grabar mientras está abierta. Si lo está, el estado indica «*X detectado. Pulsa "Iniciar grabación" para empezar.*». Si no, «*X no parece estar abierto todavía. Ábrelo y vuelve a seleccionarlo.*».

## Empezar y parar

1. Selecciona la aplicación y pulsa **Iniciar grabación**. El estado pasa a «*Escuchando X...*» y empieza a contar el tiempo.
2. Pulsa **Detener grabación** al terminar. La grabación se procesa en el momento:
   - «*Procesando audio...*»
   - «*Comprobando el modelo de transcripción...*»
   - «*Transcribiendo... N%*»
   - «*Transcripción lista y guardada*»
3. La transcripción aparece en el visor y se guarda en el historial.

También puedes empezar y parar desde el **icono de la bandeja**: clic derecho → *Iniciar grabación de X* / *Detener grabación de X*. El icono solo aparece mientras la ventana está minimizada o cerrada con la **X**. Al parar desde la bandeja se muestra la ventana, para que veas el procesado. Mientras se graba, al pasar el ratón por el icono se lee «*Loopback Transcriber - Grabando X*».

> [!NOTE]
> Si cierras la aplicación grabada mientras grabas, la grabación se para y se procesa como si hubieras pulsado **Detener grabación**.

## Qué se graba

Se graban tres fuentes a la vez:

| Pista | Qué contiene | Para qué sirve |
|---|---|---|
| Aplicación | Solo el audio que reproduce el proceso elegido y sus procesos hijos | La voz de los demás participantes. Es la fuente principal. |
| Salida de audio | Todo lo que suena por las salidas predeterminadas de Windows: la general y, si es otra, la de comunicaciones. Cada una en su pista. | Respaldo si la pista de la aplicación sale en silencio. |
| Micrófono | El micrófono predeterminado | Tu voz, en una pista aparte. |

Al parar, si la pista de la aplicación no tiene señal, se usa la pista de salida con más volumen. El micrófono se transcribe por separado y se alinea en el tiempo con la otra pista. Así la transcripción distingue entre **Participantes** y **Tú**, o tu nombre seguido de «(Tú)».

Si el micrófono no se puede abrir, la grabación continúa sin él y se avisa: «*No se ha grabado tu micrófono: la transcripción solo incluirá a los participantes.*». Si se abre pero solo capta silencio, su pista se descarta al parar y la transcripción sale sin etiquetas de quién habla.

## Mientras se genera un resumen

No se puede empezar a grabar mientras se genera un resumen con IA, porque los dos compiten por la GPU y la memoria. El botón lo indica: «*No se puede grabar mientras se genera un resumen con IA.*».

## Si no se detecta texto

Si Whisper no devuelve texto:

- **Grabación de menos de 20 segundos**: se considera que no se dijo nada. El visor muestra «*(No se detectó ningún texto en el audio)*», el estado pasa a «*Transcripción lista.*» y **no se guarda nada**: ni texto ni audio.
- **Grabación de 20 segundos o más**: se trata como un fallo (ver abajo) y el audio se conserva.

## Si algo falla

Salvo en el caso anterior, la aplicación **nunca borra el audio** de una grabación que no se ha podido transcribir. Si el procesado falla, el estado indica el error y la carpeta donde se ha guardado el audio, y en la vista de la aplicación aparece el aviso **Grabación sin procesar** con dos botones de icono, que muestran su nombre al pasar el ratón:

- **Reintentar**: vuelve a procesarla con los ajustes actuales. No está disponible mientras se graba, se procesa otra grabación o se genera un resumen.
- **Descartar**: borra el audio definitivamente, después de pedirte confirmación.

Cada grabación sin procesar aparece en la vista de su aplicación. Las recuperadas sin saber de qué aplicación eran aparecen como «Reunión» en la vista de todas.

Lo mismo ocurre si la aplicación se cierra durante una grabación, se apaga el equipo o se produce un error inesperado. Al volver a abrirla, la grabación aparece como *sin procesar*. Los casos concretos están en [Fiabilidad](../architecture/reliability.md).

## Cerrar la aplicación

- La **X** de la ventana no cierra la aplicación: la **oculta en la bandeja**, para que puedas seguir grabando. La primera vez se muestra un aviso explicándolo. Un clic en el icono vuelve a mostrar la ventana.
- Para salir de verdad: clic derecho en el icono de la bandeja → **Salir**.
- Si sales con una grabación en curso, se pregunta qué hacer:
  - **Sí**: para la grabación, la procesa con la ventana visible y sale al terminar.
  - **No**: sale ya y conserva el audio sin procesar para la próxima vez.
  - **Cancelar**: sigue grabando.
- Si se está procesando una grabación, se pregunta si quieres esperar a que termine y salir. Si respondes que no, la aplicación no se cierra.
