using System;
using System.Web;

namespace ModuloEncuestas.Infrastructure
{
    /// <summary>
    /// Quién es el usuario actual para el módulo.
    ///
    /// El módulo NO controla el acceso: eso lo hace el portal (quién entra a /Encuestas,
    /// quién administra, quién responde). Solo necesita el nombre del usuario para la
    /// bitácora, el "ya respondiste", la audiencia y las respuestas no anónimas.
    ///
    /// Por defecto usa User.Identity.Name. Si el portal identifica a la persona de otra
    /// forma (sesión propia, cookie, tabla de usuarios), asígnalo una vez en
    /// Application_Start del Global.asax del portal, por ejemplo:
    ///   ModuloEncuestas.Infrastructure.SurveyUser.Resolver =
    ///       http => http.Session == null ? null : (string)http.Session["Usuario"];
    /// Debe devolver el mismo valor que la columna Login de enc.OrgUser, o null si no hay usuario.
    /// </summary>
    public static class SurveyUser
    {
        public static Func<HttpContextBase, string> Resolver { get; set; }

        public static string Current(HttpContextBase http)
        {
            if (http == null) return null;
            var name = Resolver != null ? Resolver(http) : IdentityName(http);
            return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        }

        private static string IdentityName(HttpContextBase http)
        {
            var user = http.User;
            return user != null && user.Identity != null && user.Identity.IsAuthenticated ? user.Identity.Name : null;
        }
    }
}
