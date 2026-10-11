using System.Configuration;
using System.Web.Mvc;
using ModuloEncuestas.Infrastructure;
using ModuloEncuestas.Models.ViewModels;

namespace ModuloEncuestas.Controllers
{
    /// <summary>
    /// Páginas Razor del módulo. Toda la interacción ocurre en el navegador
    /// (jQuery) y la persistencia pasa por SurveyApiController.
    /// El acceso a cada página lo controla el portal.
    /// </summary>
    public class EncuestasController : Controller
    {
        // GET /Encuestas  (listado + "Nueva encuesta")
        public ActionResult Index()
        {
            return View(BuildModel(null));
        }

        // GET /Encuestas/Constructor/1
        public ActionResult Constructor(string id)
        {
            if (string.IsNullOrEmpty(id) && !UseMock()) return RedirectToAction("Index");
            return View(BuildModel(id));
        }

        // GET /Encuestas/Responder/1  (página que ve el colaborador)
        public ActionResult Responder(string id)
        {
            return View(BuildModel(id));
        }

        private SurveyPageViewModel BuildModel(string id)
        {
            return new SurveyPageViewModel
            {
                // Los datos mock arrancan con la encuesta "s1".
                SurveyId = string.IsNullOrEmpty(id) ? (UseMock() ? "s1" : "") : id,
                UseMockData = UseMock(),
                CurrentUser = SurveyUser.Current(HttpContext) ?? ""
            };
        }

        private static bool UseMock()
        {
            bool useMock;
            return !bool.TryParse(ConfigurationManager.AppSettings["Surveys:UseMockData"], out useMock) || useMock;
        }
    }
}
