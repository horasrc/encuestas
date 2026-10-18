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

## Formulario "Registro de citas / visitas" (Radar) — guarda con JavaScript
Ese formulario guarda sin recargar la página (`guardarCaptura()` en JavaScript), así que la cita se arma
**en el navegador** con `Areas\Citas\Scripts\cita-outlook.js`. No hay que tocar el controlador ni la base de datos.

Qué lleva la cita:
- **Asunto:** `Cita: {Tipo de cita} - {Nombre del cliente}`.
- **Notas:** cliente, ¿es cliente?, tipo de cita, acompañamiento y comentarios. Se usa el texto que ve el usuario
  en las listas, no el código interno.
- **Fecha:** el formulario no tiene hora, así que entra como evento **de todo el día**, marcado como "Libre" (no
  bloquea la agenda) y con recordatorio a las **9:00 a.m. del día anterior**.
- Solo se genera si la fecha de la cita es **hoy o futura**: una visita que ya pasó no se agenda.

### Pasos 1 y 2. Pega el código al inicio de `Page.js` (así quedó en Radar)
En Radar, `guardarCaptura()` está en `Scripts\RadarIA\Citas\Page.js` y el portal **carga ese archivo por código**
(en la consola aparece como `VM…`/`<anonymous>`), así que una etiqueta `<script>` aparte no se ejecuta.
Lo que funcionó: copiar **todo** el contenido de `cita-outlook.js` y pegarlo al **inicio** de `Page.js`.
Luego recargar con **Ctrl+F5**. Para comprobarlo, escribir `CitaOutlook` en la consola (F12): debe responder
`{descargar: ƒ, crear: ƒ}`.

En un portal que cargue los scripts de forma normal también se puede dejar como archivo aparte y cargarlo
**antes** del script que tiene `guardarCaptura()`:
```cshtml
<script src="@Url.Content("~/Scripts/cita-outlook.js")"></script>
```

### Paso 3. Llámalo cuando se guarde bien
En `guardarCaptura()`, dentro del aviso de éxito, agrega una línea **como primera instrucción** (antes de limpiar
el formulario, porque lee los textos de las listas en pantalla):
```js
simularAjax({ ... }, 700, function (response) {
    if (response.success) {
        CitaOutlook.descargar(formData);     // ← nueva línea: descarga la cita para Outlook
        // ...lo que ya hace hoy: mensaje de éxito, limpiar el formulario, etc.
    }
});
```
Cuando cambien `simularAjax` por el `$.ajax` real, deja esa línea dentro del `success`.

Si en algún momento cambian los `id` de "Tipo de cita" (`appointmentType`), "Acompañamiento" (`accompaniment`)
o el `name` de "¿Es cliente?" (`isCustomer`), ajústalos al inicio de `cita-outlook.js` (objeto `IDS`).

## Agregarlo a un formulario que guarda en el servidor (3 pasos)

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
