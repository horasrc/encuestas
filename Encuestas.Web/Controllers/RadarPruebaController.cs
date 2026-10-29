using System.Web.Mvc;
using ModuloEncuestas.Controllers;
using ModuloEncuestas.Models.ViewModels;

namespace Encuestas.Web.Controllers
{
    /// <summary>
    /// SOLO PRUEBAS: simula RadarController del portal, que carga los módulos como vistas
    /// parciales (ej. MiTuberia(string segmento)). En el portal real no se copia: se agrega
    /// la acción Encuestas a su RadarController (ver INTEGRACION.md).
    ///   /RadarPrueba                      página tipo Radar con menú de segmentos
    ///   /RadarPrueba/Encuestas?segmento=  la vista parcial del módulo
    /// </summary>
    public class RadarPruebaController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        // Igual a lo que va en RadarController del portal.
        public PartialViewResult Encuestas(string segmento, string vista)
        {
            return PartialView(EncuestasController.VistaParcial, new SurveyEmbedViewModel { Segmento = segmento, Vista = vista });
        }

        // Usuario simulado (cookie) para probar el tramo de control; ver Global.asax.cs.
        public ActionResult ComoUsuario(string usuario)
        {
            Response.Cookies.Add(new System.Web.HttpCookie("rp-usuario", usuario ?? "") { HttpOnly = true });
            return RedirectToAction("Index");
        }
    }
}
