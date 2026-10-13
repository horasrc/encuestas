using System;
using System.Web.Mvc;
using ModuloCitas.Models;
using ModuloCitas.Services;

namespace ModuloCitas.Controllers
{
    /// <summary>
    /// Ejemplo de formulario de citas que, al aceptar, entrega la cita a Outlook (.ics).
    ///   GET  /Citas                 formulario + próximas citas
    ///   POST /Citas/Guardar         guarda y redirige a la confirmación
    ///   GET  /Citas/Confirmada/{id} confirmación; descarga sola el .ics
    ///   GET  /Citas/Outlook/{id}    el archivo .ics (abre Outlook)
    /// Para tu portal solo necesitas la acción Outlook y CalendarioIcs.cs (ver CITAS-OUTLOOK.md).
    /// </summary>
    public class CitasController : Controller
    {
        public ActionResult Index()
        {
            ViewBag.Proximas = CitaStore.Proximas(Usuario());
            return View(new CitaModel
            {
                Fecha = DateTime.Today.AddDays(1),
                HoraInicio = new TimeSpan(10, 0, 0),
                HoraFin = new TimeSpan(10, 30, 0),
                Recordatorio = 15
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public ActionResult Guardar([Bind(Exclude = "Id,Usuario")] CitaModel model)
        {
            if (!ModelState.IsValid)
            {
                ViewBag.Proximas = CitaStore.Proximas(Usuario());
                return View("Index", model);
            }
            model.Usuario = Usuario();
            var id = CitaStore.Guardar(model);   // en el portal: lo que ya hace tu formulario al guardar
            return RedirectToAction("Confirmada", new { id = id });
        }

        public ActionResult Confirmada(int id)
        {
            var cita = CitaStore.Obtener(id, Usuario());
            if (cita == null) return HttpNotFound();
            return View(cita);
        }

        /// <summary>Lo que abre Outlook. Copia esta acción al controlador de citas del portal.</summary>
        public ActionResult Outlook(int id)
        {
            var cita = CitaStore.Obtener(id, Usuario());
            if (cita == null) return HttpNotFound();

            var ics = CalendarioIcs.Crear(new EventoCalendario
            {
                Uid = "cita-" + cita.Id + "@portal",
                Asunto = cita.Asunto,
                Inicio = cita.Inicio,
                Fin = cita.Fin,
                Lugar = cita.Lugar,
                Descripcion = cita.Notas,
                RecordatorioMinutos = cita.Recordatorio
            });
            return File(ics, CalendarioIcs.ContentType, CalendarioIcs.NombreArchivo("cita-" + cita.Id));
        }

        private string Usuario()
        {
            return User != null && User.Identity.IsAuthenticated ? User.Identity.Name : "";
        }
    }
}
