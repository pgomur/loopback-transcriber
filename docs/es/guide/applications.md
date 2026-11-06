# Aplicaciones

La pantalla **Aplicaciones** permite elegir qué programas, además de Teams, aparecen en la barra lateral para poder grabarlos.

## Añadir una aplicación

1. Abre la aplicación que quieres grabar y haz que **reproduzca audio**: entra en una llamada o reproduce un vídeo.
2. En **Aplicaciones**, la sección **Procesos detectados con audio** muestra los procesos que tienen una sesión de audio en ese momento. Se actualiza sola cada 2 segundos mientras la pantalla está abierta, y también con **Actualizar**. Las aplicaciones que ya se vigilan no aparecen.
3. Pulsa **Añadir**. La aplicación pasa a **Añadidas para escuchar** y aparece en la barra lateral.

El nombre que se muestra es la descripción del ejecutable (por ejemplo, «Zoom Meetings») o, si no la tiene, el nombre del proceso. Ese nombre es también el de la carpeta de sus transcripciones.

Las aplicaciones añadidas se guardan en los ajustes, con su nombre de proceso (por ejemplo `zoom` o `chrome`), y se vigilan igual que Teams.

> [!TIP]
> Para Google Meet u otras reuniones en el navegador, añade el navegador (Chrome, Edge…). Ten en cuenta que se graba **todo** el audio del navegador, no solo la pestaña de la reunión: cierra otras pestañas que reproduzcan sonido.

## Quitar una aplicación

Pulsa **Quitar** en **Añadidas para escuchar**. Si la aplicación tiene transcripciones guardadas, no se puede quitar: elimínalas antes desde el historial. Teams viene de serie y no se puede quitar.

## Cómo se elige el proceso

Muchas aplicaciones tienen varios procesos con el mismo nombre (Teams, los navegadores). La aplicación:

1. Busca los procesos con ese nombre y se queda con el proceso raíz del árbol, el que no tiene como padre a otro de ellos.
2. Si alguno de esos procesos tiene una sesión de audio activa, prefiere ese.
3. Graba ese proceso **y todos sus procesos hijos**, que es donde suelen estar el audio de las llamadas y las pestañas del navegador.
