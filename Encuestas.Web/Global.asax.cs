using System.Configuration;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using ModuloEncuestas.Infrastructure;

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

            if (string.Equals(ConfigurationManager.AppSettings["Prueba:SimularTramo"], "true", System.StringComparison.OrdinalIgnoreCase))
                SimularTramoDeControl();
        }

        /// <summary>
        /// SOLO PRUEBAS (Prueba:SimularTramo = true): simula lo que hará el portal con su tramo de control.
        /// El usuario sale de la cookie "rp-usuario" (ver /RadarPrueba); sin cookie, el usuario de Windows sin restricciones.
        /// En el portal real esto se arma con sus propias tablas o servicios (ver INTEGRACION.md).
        /// </summary>
        private static void SimularTramoDeControl()
        {
            System.Func<HttpContextBase, string> simulado = http =>
            {
                var c = http.Request.Cookies["rp-usuario"];
                return c == null || string.IsNullOrEmpty(c.Value) ? null : c.Value;
            };
            SurveyUser.Resolver = http =>
            {
                var u = simulado(http);
                return u != null ? "PRUEBA\\" + u : (http.User.Identity.IsAuthenticated ? http.User.Identity.Name : null);
            };
            SurveyAccess.Resolver = http =>
            {
                switch (simulado(http))
                {
                    case null: return SurveyScope.Unrestricted();
                    case "jefe": return new SurveyScope { CanAdminister = true, Segments = new[] { "PIPB", "Empresarial" }, Team = new[] { "PRUEBA\\ana", "PRUEBA\\jefe" } };
                    case "ana": return new SurveyScope { Segments = new[] { "PIPB" } };
                    case "beto": return new SurveyScope { Segments = new[] { "Empresarial" } };
                    default: return new SurveyScope { Segments = new string[0] };
                }
            };
        }
    }
}
