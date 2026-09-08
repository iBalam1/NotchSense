# NotchSense

Monitor minimalista para Windows 11 con tres anillos de CPU, GPU y RAM. Tiene un modo normal con detalle al pasar el cursor y un modo discreto, transparente y sin interacción.

## Requisitos

- Windows 11.
- .NET 8 SDK para compilar.
- Para temperaturas, algunos equipos/controladores pueden requerir permisos elevados. La frecuencia de CPU tiene un respaldo usando la API de energía de Windows cuando LibreHardwareMonitor no entrega el reloj. Si una temperatura no está disponible, se muestra `—`. Para ciertos sensores de CPU, LibreHardwareMonitor puede requerir PawnIO y ejecución como administrador; consulta `ADVANCED.md`.

## Compilar y ejecutar

```powershell
dotnet restore
dotnet run
```

## Controles

Usa el icono de la bandeja para cambiar entre modo normal y discreto, elegir el borde izquierdo o derecho, activar el inicio con Windows o salir.

## Notas de diseño

- Los sensores se leen fuera del hilo de interfaz cada segundo.
- La GPU se decide una vez por sesión para que el valor no salte entre iGPU y dGPU.
- Después de suspensión/reanudación, se recrea el lector; mientras tanto se conservan valores atenuados en vez de mostrar ceros falsos.
- El modo discreto activa click-through nativo: los clics pasan a la ventana inferior.



## Inspiración

La creación de NotchSense se inspiró en **Codenotch** de VinzDG, especialmente en su concepto de notch minimalista integrado al borde de la pantalla y su interacción al pasar el cursor. Codenotch es un proyecto independiente y no forma parte de NotchSense.

## Idioma

La interfaz detecta automáticamente el idioma de Windows mediante la cultura de interfaz actual. Español se muestra en español; otros idiomas usan inglés como idioma de respaldo.

## Bordes

NotchSense admite únicamente los bordes izquierdo y derecho. La configuración de versiones anteriores se migra conservando `StartWithWindows`; `Top` y `Bottom` se convierten a `Right`.
