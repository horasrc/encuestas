namespace ModuloEncuestas.Models.ViewModels
{
    /// <summary>
    /// Datos mínimos que necesita la vista para arrancar el JavaScript.
    /// El contenido de la encuesta se carga vía SurveyApi (mock o servidor).
    /// </summary>
    public class SurveyPageViewModel
    {
        public string SurveyId { get; set; }
        public bool UseMockData { get; set; }
        public string CurrentUser { get; set; }

        /// <summary>Segmento del portal en que se trabaja (null = sin segmento).</summary>
        public string Segment { get; set; }

        /// <summary>La página se muestra dentro del portal (marco de la vista parcial): sin menú propio.</summary>
        public bool Embedded { get; set; }

        /// <summary>Quien la ve puede administrar (lo decide el portal en SurveyAccess).</summary>
        public bool CanAdminister { get; set; }
    }

    /// <summary>
    /// Modelo de la vista parcial que devuelve el portal (ej. RadarController) para mostrar
    /// el módulo: Views/Encuestas/_Encuestas.cshtml.
    /// </summary>
    public class SurveyEmbedViewModel
    {
        /// <summary>Segmento que recibe la acción del portal.</summary>
        public string Segmento { get; set; }

        /// <summary>
        /// Qué mostrar: "admin" (listado y constructor), "responder" (encuestas pendientes de
        /// responder) o vacío = según SurveyScope.CanAdminister.
        /// </summary>
        public string Vista { get; set; }

        /// <summary>Encuesta a abrir directamente para responder (opcional, con Vista = "responder").</summary>
        public string EncuestaId { get; set; }
    }
}
