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
    }
}
