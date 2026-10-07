using System.Configuration;
using System.Web.Mvc;

namespace Encuestas.Web.Infrastructure
{
    /// <summary>
    /// Restringe la administración de encuestas (constructor, resultados, API de edición).
    /// Roles en Web.config -> appSettings "Surveys:AdminRoles", separados por coma
    /// (ej. "DOMINIO\Encuestas-Admin,DOMINIO\Comunicacion"). Vacío = cualquier usuario autenticado.
    ///
    /// Para peticiones AJAX responde JSON (401/403) en lugar de redirigir, para que
    /// survey-api.js pueda mostrar el mensaje.
    /// </summary>
    public class SurveyAdminAuthorizeAttribute : AuthorizeAttribute
    {
        public SurveyAdminAuthorizeAttribute()
        {
            Roles = ConfigurationManager.AppSettings["Surveys:AdminRoles"] ?? "";
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext)
        {
            var http = filterContext.HttpContext;
            var authenticated = http.User != null && http.User.Identity.IsAuthenticated;

            if (http.Request.IsAjaxRequest())
            {
                filterContext.Result = new JsonNetResult(new
                {
                    ok = false,
                    message = authenticated ? "No tienes permisos para administrar encuestas." : "Tu sesión expiró. Vuelve a iniciar sesión.",
                    errors = new string[0]
                }, authenticated ? 403 : 401);
            }
            else if (authenticated)
            {
                filterContext.Result = new HttpStatusCodeResult(403, "No tienes permisos para administrar encuestas.");
            }
            else
            {
                base.HandleUnauthorizedRequest(filterContext);
            }
        }
    }
}
