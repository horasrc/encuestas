using System.Web.Mvc;

namespace Encuestas.Web.Controllers
{
    /// <summary>
    /// Solo existe en este proyecto de ejemplo: la raíz del sitio lleva al módulo de encuestas.
    /// En el portal real no se copia (el portal ya tiene su página de inicio).
    /// </summary>
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return RedirectToAction("Index", "Encuestas", new { area = "Encuestas" });
        }
    }
}
