# Citas → calendario de Outlook

Al dar **Aceptar** en un formulario de citas, la cita se guarda y se descarga un archivo de calendario
(`.ics`). Al abrirlo, Outlook muestra la cita ya llena (asunto, fecha, hora, lugar, notas y recordatorio)
y la persona solo da **Guardar y cerrar**.

- No necesita internet, Exchange, contraseñas ni permisos especiales. Funciona con .NET 4.5.
- Un navegador no puede escribir directo en el Outlook de la persona: siempre hace falta abrir el archivo
  (un clic). En Edge/Chrome se puede marcar "Abrir siempre archivos de este tipo" en la descarga, o Sistemas
  puede configurarlo para todos por directiva de grupo; así Outlook se abre solo.

## Probarlo en este proyecto
Ejecuta el sitio (F5 en Visual Studio) y abre **`/Citas`** (también está en el menú lateral).
Llena el formulario → **Aceptar** → se descarga `cita-N.ics` → ábrelo → Outlook.

El ejemplo está en `Encuestas.Web\Areas\Citas\`:
```
Services\CalendarioIcs.cs     genera el .ics  ← lo único que necesitas copiar al portal
Controllers\CitasController   formulario, Guardar, Confirmada y Outlook (descarga del .ics)
Models\CitaModel.cs           campos y validaciones del formulario de ejemplo
Services\CitaStore.cs         guarda en memoria SOLO para la demostración
Views\Citas\                  Index (formulario) y Confirmada (descarga automática + botón)
```

## Agregarlo al formulario de citas del portal (3 pasos)

### 1. Copia `CalendarioIcs.cs`
Cópialo a una carpeta del proyecto del portal (por ejemplo `Helpers\`) e inclúyelo en el proyecto.
Puedes dejarle el espacio de nombres `ModuloCitas.Services` o cambiarlo por el del portal.

### 2. Agrega la acción `Outlook` al controlador de citas del portal
Cambia `_citas.Obtener` y los nombres de campos por los de tu formulario:
```csharp
// using ModuloCitas.Services;
public ActionResult Outlook(int id)
{
    var cita = _citas.Obtener(id);                       // ← como el portal lee una cita
    if (cita == null || cita.Usuario != User.Identity.Name)   // ← nadie descarga citas de otros
        return HttpNotFound();

    var ics = CalendarioIcs.Crear(new EventoCalendario
    {
        Uid = "cita-" + cita.Id + "@portal",              // mismo id = misma cita si se abre dos veces
        Asunto = cita.Asunto,
        Inicio = cita.Fecha.Date + cita.HoraInicio,      // ← si fecha y hora están en campos separados
        Fin = cita.Fecha.Date + cita.HoraFin,
        Lugar = cita.Lugar,
        Descripcion = cita.Notas,
        RecordatorioMinutos = 15                         // 0 = sin recordatorio
    });
    return File(ics, CalendarioIcs.ContentType, CalendarioIcs.NombreArchivo("cita-" + cita.Id));
}
```

### 3. Después de guardar, descarga el archivo
En la acción que hoy guarda la cita (la del botón **Aceptar**), al terminar de guardar redirige a una página de
confirmación (como `Confirmada.cshtml` del ejemplo) que tenga el botón y la descarga automática:
```cshtml
<a id="btnOutlook" class="btn btn-primary" href="@Url.Action("Outlook", new { id = Model.Id })">
    <i class="bi bi-calendar-plus"></i> Agregar a mi calendario de Outlook
</a>
@section scripts {
<script>
    setTimeout(function () { window.location.href = document.getElementById('btnOutlook').href; }, 400);
</script>
}
```
Si el portal ya tiene su propia página de "cita guardada", solo agrégale ese botón y el script.

## Detalles
- **Hora:** se usa la hora del servidor y se convierte a UTC; Outlook la muestra en la zona horaria de cada
  persona. Si el servidor y los usuarios están en la misma zona (lo normal), la cita queda a la hora elegida.
- **Si cambian la cita en el portal:** el usuario vuelve a descargar el `.ics`; como el `Uid` es el mismo, Outlook
  ofrece actualizar la cita existente en lugar de duplicarla.
- **Que llegue sola al calendario, sin descargar nada:** requiere que el portal envíe correos (SMTP de Exchange):
  se envía la misma cita como invitación por correo. Hay que pedir a Sistemas el servidor de correo y el permiso
  para enviar.
