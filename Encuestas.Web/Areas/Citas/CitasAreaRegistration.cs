using System.Web.Mvc;

namespace ModuloCitas
{
    /// <summary>
    /// Área "Citas" (ejemplo): formulario de citas que agrega la cita al calendario de Outlook.
    /// URLs: /Citas, /Citas/Confirmada/{id}, /Citas/Outlook/{id}.
    /// </summary>
    public class CitasAreaRegistration : AreaRegistration
    {
        public override string AreaName
        {
            get { return "Citas"; }
        }

        public override void RegisterArea(AreaRegistrationContext context)
        {
            context.MapRoute(
                "Citas_Paginas",
                "Citas/{action}/{id}",
                new { controller = "Citas", action = "Index", id = UrlParameter.Optional },
                new[] { "ModuloCitas.Controllers" });
        }
    }
}
