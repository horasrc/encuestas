using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;

namespace ModuloEncuestas.Infrastructure
{
    /// <summary>
    /// Lo que el PORTAL permite a la persona que inició sesión (su tramo de control).
    /// El módulo no tiene administración de usuarios: solo aplica lo que aquí le digan.
    /// </summary>
    public class SurveyScope
    {
        /// <summary>Puede crear, editar, publicar y ver resultados.</summary>
        public bool CanAdminister { get; set; }

        /// <summary>Segmentos en los que puede trabajar (ej. los que recibe RadarController). Null = todos.</summary>
        public IList<string> Segments { get; set; }

        /// <summary>
        /// Usuarios de su tramo de control (mismo valor que SurveyUser, ej. "DOMINIO\\usuario").
        /// En resultados solo ve las respuestas de estas personas. Null = ve todas.
        /// </summary>
        public IList<string> Team { get; set; }

        /// <summary>Sin restricciones: lo que pasa cuando el portal no asigna SurveyAccess.Resolver.</summary>
        public static SurveyScope Unrestricted()
        {
            return new SurveyScope { CanAdminister = true };
        }
    }

    /// <summary>
    /// Punto de conexión con el portal para el tramo de control. Se asigna una vez en
    /// Application_Start del Global.asax del portal, por ejemplo:
    ///
    ///   ModuloEncuestas.Infrastructure.SurveyAccess.Resolver = http =&gt; new SurveyScope
    ///   {
    ///       CanAdminister = EsAdministrador(http),
    ///       Segments = SegmentosDelUsuario(http),
    ///       Team = UsuariosDeSuTramo(http)
    ///   };
    ///
    /// Si no se asigna, el módulo no restringe nada (todos administran, ven todo y responden todo).
    ///
    /// El segmento actual llega en cada petición: el parámetro "segmento" de la vista parcial o
    /// el encabezado X-Survey-Segment que envía survey-api.js. Solo se acepta si está en Segments.
    /// </summary>
    public static class SurveyAccess
    {
        public const string SegmentHeader = "X-Survey-Segment";
        private const string CacheKey = "ModuloEncuestas.SurveyScope";

        public static Func<HttpContextBase, SurveyScope> Resolver { get; set; }

        /// <summary>Alcance de la persona en esta petición (se calcula una sola vez por petición).</summary>
        public static SurveyScope Current(HttpContextBase http)
        {
            if (Resolver == null || http == null) return SurveyScope.Unrestricted();
            var cached = http.Items[CacheKey] as SurveyScope;
            if (cached != null) return cached;
            var scope = Resolver(http) ?? new SurveyScope();
            http.Items[CacheKey] = scope;
            return scope;
        }

        /// <summary>
        /// Segmento de la petición, o null si no se indicó. Lanza HttpException 403 si la persona no
        /// tiene ese segmento en su alcance.
        /// </summary>
        public static string CurrentSegment(HttpContextBase http, string explicitSegment = null)
        {
            var segment = explicitSegment;
            if (string.IsNullOrWhiteSpace(segment) && http != null && http.Request != null)
            {
                // El encabezado va codificado (encodeURIComponent) para admitir acentos.
                var header = http.Request.Headers[SegmentHeader];
                segment = header != null ? Uri.UnescapeDataString(header) : http.Request.QueryString["segmento"];
            }
            segment = string.IsNullOrWhiteSpace(segment) ? null : segment.Trim();
            if (segment != null && !IsAllowed(Current(http), segment))
                throw new HttpException(403, "El segmento «" + segment + "» no está en tu alcance.");
            return segment;
        }

        public static bool IsAllowed(SurveyScope scope, string segment)
        {
            return scope.Segments == null || scope.Segments.Any(s => string.Equals(s, segment, StringComparison.OrdinalIgnoreCase));
        }
    }
}
