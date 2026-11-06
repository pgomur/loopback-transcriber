# Transcripciones y resúmenes

## Dónde están

- **Transcripciones** (barra lateral): todas las transcripciones guardadas, de cualquier aplicación.
- **Historial**, en la vista de cada aplicación: solo las de esa aplicación.

Cada elemento muestra el título (el del resumen, si lo hay, o el nombre de la aplicación), la fecha y la duración del audio (`dd/MM HH:mm · m:ss`, o `h:mm:ss` a partir de una hora) y un extracto de 100 caracteres. El extracto sale del resumen si existe y, si no, del principio de la transcripción. Si no hay ninguno de los dos, muestra «*(Transcripción vacía)*». La lista se actualiza sola cuando cambia la carpeta de transcripciones, también si copias o borras ficheros a mano.

**Ver carpeta** abre la carpeta de transcripciones en el Explorador.

> [!NOTE]
> El **Historial** de una aplicación muestra las grabaciones cuya carpeta lleva exactamente su nombre. Si el nombre de la aplicación contiene caracteres que no se admiten en un nombre de fichero (`\ / : * ? " < > |`), sus grabaciones se guardan con esos caracteres cambiados por `_` y solo aparecen en **Transcripciones**.

## Buscar

Las dos listas tienen un cuadro **Buscar en transcripciones y resúmenes**. Busca en el título, el nombre de la aplicación, la fecha (`dd/MM/yyyy HH:mm` y `yyyy-MM-dd`), el resumen y el texto completo de la transcripción.

- Sin distinguir mayúsculas ni tildes: `reunion` encuentra «Reunión».
- Varias palabras: deben aparecer **todas**, en cualquier parte.
- Entre comillas, una frase exacta: `"próxima semana"`.
- La búsqueda empieza 300 ms después de que dejes de escribir. Si nada coincide, la lista indica «*Sin resultados*».

## Filtrar

El botón **Filtrar** abre un panel para acotar la lista:

- **Desde** / **Hasta**: fechas incluidas.
- **Duración mínima (minutos)**: deja fuera las grabaciones más cortas. Las que no tienen audio guardado tampoco aparecen con este filtro.

Los filtros se aplican al momento. **Aplicar** cierra el panel y **Limpiar filtros** los quita todos. Mientras hay un filtro activo, el botón **Filtrar** aparece resaltado. Cada lista (**Transcripciones** y el **Historial** de cada aplicación) tiene sus propios filtros y su propia búsqueda.

## El visor

Al abrir una transcripción, el visor tiene dos pestañas:

- **Transcripción**: el texto en forma de conversación. Cada párrafo indica quién habla (**Participantes**, **Tú** o *tu nombre (Tú)*). Como en un chat, tus intervenciones van a la derecha y las de los participantes a la izquierda.
- **Resumen IA**: el resumen, si existe.

**Limpiar visor** lo cierra. Si la grabación solo tiene audio, el visor lo indica en lugar del texto.

### Generar un resumen

En la pestaña **Resumen IA**, pulsa **Resumir**. El progreso se muestra en porcentaje. El resumen se escribe en el **idioma de la aplicación**, aunque la reunión sea en otro, y contiene:

- **Título**: ocho palabras como máximo.
- **Propósito**: de qué trata la conversación y para qué se tuvo.
- **Puntos clave**: entre 5 y 12, con datos concretos.
- **Temas**: solo si se trataron varios asuntos o uno con mucho detalle.
- **Objetivos**: lo que alguien quiere conseguir o se compromete a hacer.
- **Próximos pasos**: reuniones, fechas y seguimientos acordados.

Las secciones sin contenido se omiten. Una vez generado, el título del resumen sustituye al nombre de la aplicación en la lista. **Regenerar** sustituye el resumen por uno nuevo.

Los títulos de las secciones se muestran siempre en el idioma actual de la aplicación, cada uno con su icono. El contenido se queda en el idioma en que se generó.

> [!NOTE]
> No se pueden generar resúmenes mientras se graba o se procesa una grabación. Si cierras la aplicación mientras se genera uno, se pide confirmación porque ese resumen se perdería.

Si falta el modelo de resumen, el visor indica «*Falta el modelo de resumen. Descárgalo en Configuración para poder generar resúmenes.*». Una grabación que solo tiene audio no se puede resumir.

Los resúmenes con IA pueden contener errores: revísalos antes de usarlos.

### Volver a transcribir

**Volver a transcribir** procesa de nuevo el audio guardado con los ajustes actuales (idioma, tu nombre) y reemplaza el texto. Antes pide confirmación. Úsalo, por ejemplo, después de cambiar el idioma de las reuniones.

- Solo está en el visor del **Historial** de cada aplicación, no en el de **Transcripciones**.
- Solo está disponible si la grabación tiene su audio (`.wav`), y no mientras se graba o se procesa otra grabación.
- El resumen **no** se modifica. Puedes regenerarlo después.
- La cabecera del fichero se conserva.

## Eliminar

Marca las casillas de las transcripciones que quieras borrar y pulsa **Eliminar**. Se pide confirmación. Se borra la carpeta de cada grabación entera: texto, audio y resumen. Si era la última de esa aplicación, se borra también la carpeta de la aplicación. No se puede deshacer.

No se puede eliminar una transcripción mientras se está generando su resumen.

## Ficheros de cada grabación

Cada grabación guardada es una carpeta dentro de la carpeta de transcripciones:

```
transcripts\
  Teams\
    Teams_20261018_093000\
      Teams_20261018_093000.md               texto (Markdown)
      Teams_20261018_093000.wav              audio de la reunión
      Teams_20261018_093000_mic.wav          tu micrófono, si se grabó
      Teams_20261018_093000.recording.json   desfase del micrófono
      Teams_20261018_093000.summary.json     resumen, si se generó
```

El formato de cada fichero se describe en [Datos en disco](../architecture/data-on-disk.md).
