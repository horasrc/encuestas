using System.Web.Mvc;
using System.Web.Routing;

namespace Encuestas.Web
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            // No anunciar la versión de MVC en las respuestas (encabezado X-AspNetMvc-Version).
            MvcHandler.DisableMvcResponseHeader = true;
            // X-Frame-Options ya lo envía Web.config para todo el sitio; evita que el antiforgery lo duplique.
            System.Web.Helpers.AntiForgeryConfig.SuppressXFrameOptionsHeader = true;
            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(RouteTable.Routes);
        }
    }
}
