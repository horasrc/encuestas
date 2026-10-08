using System.Web.Mvc;

namespace ModuloEncuestas
{
    /// <summary>
    /// Registro del Área "Encuestas". MVC la encuentra sola al arrancar si el Global.asax
    /// del proyecto llama a AreaRegistration.RegisterAllAreas() (las plantillas de MVC lo traen).
    ///
    /// URLs:
    ///   /Encuestas                      listado (administración)
    ///   /Encuestas/Constructor/{id}     constructor
    ///   /Encuestas/Responder/{id}       página para responder
    ///   /Encuestas/Api/{accion}/{id}    API JSON que usa el JavaScript del módulo
    ///
    /// Las rutas solo buscan controladores en ModuloEncuestas.Controllers, así que no chocan
    /// con controladores del portal aunque se llamen igual.
    /// </summary>
    public class EncuestasAreaRegistration : AreaRegistration
    {
        public override string AreaName
        {
            get { return "Encuestas"; }
        }

        public override void RegisterArea(AreaRegistrationContext context)
        {
            var namespaces = new[] { "ModuloEncuestas.Controllers" };

            // La API va primero: "Api" no debe interpretarse como una acción del controlador Encuestas.
            context.MapRoute(
                "Encuestas_Api",
                "Encuestas/Api/{action}/{id}",
                new { controller = "SurveyApi", id = UrlParameter.Optional },
                namespaces);

            context.MapRoute(
                "Encuestas_Paginas",
                "Encuestas/{action}/{id}",
                new { controller = "Encuestas", action = "Index", id = UrlParameter.Optional },
                namespaces);
        }
    }
}
