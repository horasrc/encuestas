using System;
using System.Web.Helpers;
using System.Web.Mvc;

namespace Encuestas.Web.Infrastructure
{
    /// <summary>
    /// Equivalente a [ValidateAntiForgeryToken] para peticiones JSON: el token del
    /// formulario llega en el encabezado "RequestVerificationToken" (lo agrega
    /// survey-api.js a partir del campo que genera @Html.AntiForgeryToken() en
    /// _SurveyConfig.cshtml) y el de la cookie se valida contra él.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class ValidateJsonAntiForgeryTokenAttribute : FilterAttribute, IAuthorizationFilter
    {
        public const string HeaderName = "RequestVerificationToken";

        /// <summary>
        /// Se ejecuta DESPUÉS de [SurveyAdminAuthorize] (Order por defecto -1): primero se exige
        /// usuario autenticado (lo que dispara el desafío de Windows) y luego se valida el token,
        /// que está ligado a ese usuario.
        /// </summary>
        public ValidateJsonAntiForgeryTokenAttribute()
        {
            Order = 100;
        }

        public void OnAuthorization(AuthorizationContext filterContext)
        {
            var request = filterContext.HttpContext.Request;
            if (!string.Equals(request.HttpMethod, "POST", StringComparison.OrdinalIgnoreCase)) return;

            var cookie = request.Cookies[AntiForgeryConfig.CookieName];
            try
            {
                AntiForgery.Validate(cookie != null ? cookie.Value : null, request.Headers[HeaderName]);
            }
            catch (HttpAntiForgeryException)
            {
                filterContext.Result = new JsonNetResult(new
                {
                    ok = false,
                    message = "La página expiró por seguridad. Recárgala e inténtalo de nuevo.",
                    errors = new string[0]
                }, 400);
            }
        }
    }
}
