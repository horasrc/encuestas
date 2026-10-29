using System.Configuration;
using System.Web;
using System.Web.Mvc;
using ModuloEncuestas.Infrastructure;
using ModuloEncuestas.Models.ViewModels;

namespace ModuloEncuestas.Controllers
{
    /// <summary>
    /// Páginas Razor del módulo. Toda la interacción ocurre en el navegador
    /// (jQuery) y la persistencia pasa por SurveyApiController.
    ///
    /// Quién administra, en qué segmento y qué tramo de control: lo decide el portal
    /// (Infrastructure.SurveyAccess). Parámetros comunes:
    ///   segmento = segmento del portal (ej. el que recibe RadarController)
    ///   embed=1  = la página va dentro del marco de la vista parcial _Encuestas.cshtml (sin menú propio)
    /// </summary>
    public class EncuestasController : Controller
    {
        /// <summary>Vista parcial para el portal: return PartialView(EncuestasController.VistaParcial, new SurveyEmbedViewModel { Segmento = segmento });</summary>
        public const string VistaParcial = "~/Areas/Encuestas/Views/Encuestas/_Encuestas.cshtml";

        // GET /Encuestas  (listado + "Nueva encuesta"; quien no administra ve sus encuestas pendientes)
        public ActionResult Index(string segmento, bool embed = false)
        {
            var model = BuildModel(null, segmento, embed);
            if (model == null) return Forbidden(segmento);
            return model.CanAdminister ? (ActionResult)View(model) : View("Pendientes", model);
        }

        // GET /Encuestas/Constructor/1
        public ActionResult Constructor(string id, string segmento, bool embed = false)
        {
            var model = BuildModel(id, segmento, embed);
            if (model == null || !model.CanAdminister) return Forbidden(segmento);
            if (string.IsNullOrEmpty(id) && !UseMock()) return RedirectToAction("Index", new { segmento, embed });
            return View(model);
        }

        // GET /Encuestas/Pendientes  (encuestas que la persona puede responder en el segmento)
        public ActionResult Pendientes(string segmento, bool embed = false)
        {
            var model = BuildModel(null, segmento, embed);
            return model == null ? Forbidden(segmento) : View(model);
        }

        // GET /Encuestas/Responder/1  (página que ve el colaborador)
        public ActionResult Responder(string id, string segmento, bool embed = false)
        {
            var model = BuildModel(id, segmento, embed);
            return model == null ? Forbidden(segmento) : View(model);
        }

        /// <summary>Null si el segmento no está en el alcance de la persona.</summary>
        private SurveyPageViewModel BuildModel(string id, string segmento, bool embed)
        {
            string segment;
            try
            {
                segment = SurveyAccess.CurrentSegment(HttpContext, segmento);
            }
            catch (HttpException)
            {
                return null;
            }
            if (embed) ViewBag.Layout = "~/Areas/Encuestas/Views/Shared/_LayoutEmbed.cshtml";
            return new SurveyPageViewModel
            {
                // Los datos mock arrancan con la encuesta "s1".
                SurveyId = string.IsNullOrEmpty(id) ? (UseMock() ? "s1" : "") : id,
                UseMockData = UseMock(),
                CurrentUser = SurveyUser.Current(HttpContext) ?? "",
                Segment = segment,
                Embedded = embed,
                CanAdminister = SurveyAccess.Current(HttpContext).CanAdminister
            };
        }

        private ActionResult Forbidden(string segmento)
        {
            Response.StatusCode = 403;
            Response.TrySkipIisCustomErrors = true;
            return Content("No tienes acceso a las encuestas" + (string.IsNullOrWhiteSpace(segmento) ? "" : " del segmento «" + HttpUtility.HtmlEncode(segmento) + "»") + ".", "text/plain");
        }

        private static bool UseMock()
        {
            bool useMock;
            return !bool.TryParse(ConfigurationManager.AppSettings["Surveys:UseMockData"], out useMock) || useMock;
        }
    }
}
