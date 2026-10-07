using System;
using System.IO;
using System.Web.Hosting;
using System.Web.Mvc;

namespace Encuestas.Web.Infrastructure
{
    /// <summary>
    /// URLs de archivos estáticos con "huella" de versión para evitar que el navegador
    /// use JS/CSS viejos después de una actualización:
    ///   @Url.Versioned("~/Scripts/survey/survey-app.js")  ->  /Scripts/survey/survey-app.js?v=638...
    /// La versión es la fecha de modificación del archivo, así que cambia sola al desplegar.
    /// </summary>
    public static class AssetUrlExtensions
    {
        public static string Versioned(this UrlHelper url, string contentPath)
        {
            var resolved = url.Content(contentPath);
            var physical = HostingEnvironment.MapPath(contentPath);
            if (physical == null || !File.Exists(physical)) return resolved;
            var stamp = File.GetLastWriteTimeUtc(physical).Ticks.ToString("x");
            return resolved + (resolved.IndexOf('?') >= 0 ? "&" : "?") + "v=" + stamp;
        }
    }
}
