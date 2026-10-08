using System.Web.Mvc;
using System.Web.Routing;

namespace Encuestas.Web
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // Las rutas del módulo (/Encuestas/...) las registra el Área en EncuestasAreaRegistration.
            var route = routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional },
                namespaces: new[] { "Encuestas.Web.Controllers" }
            );
            // Sin esto, MVC busca en todos los espacios de nombres si no encuentra el controlador
            // en Encuestas.Web.Controllers, y los controladores del Área quedarían accesibles también
            // con URLs fuera de /Encuestas (por ejemplo /SurveyApi/List).
            route.DataTokens["UseNamespaceFallback"] = false;
        }
    }
}
