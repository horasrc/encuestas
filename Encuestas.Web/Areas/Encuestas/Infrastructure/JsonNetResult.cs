using System;
using System.IO;
using System.Web;
using System.Web.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ModuloEncuestas.Infrastructure
{
    /// <summary>
    /// Serializa con Json.NET en camelCase (el JavaScriptSerializer de MVC
    /// usa PascalCase y formatos de fecha /Date()/ difíciles de consumir).
    /// </summary>
    public class JsonNetResult : ActionResult
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatString = "yyyy-MM-ddTHH:mm:ss"
        };

        private readonly object _data;
        private readonly int _statusCode;

        public JsonNetResult(object data, int statusCode = 200)
        {
            _data = data;
            _statusCode = statusCode;
        }

        public override void ExecuteResult(ControllerContext context)
        {
            var response = context.HttpContext.Response;
            response.StatusCode = _statusCode;
            response.ContentType = "application/json";
            response.ContentEncoding = System.Text.Encoding.UTF8;
            // Datos vivos (elegibilidad, resultados): que ni el navegador ni proxies los guarden.
            response.Cache.SetCacheability(HttpCacheability.NoCache);
            response.Cache.SetNoStore();
            response.Write(JsonConvert.SerializeObject(_data, Settings));
        }

        /// <summary>Lee el cuerpo JSON de la petición y lo deserializa (sin depender del model binder).</summary>
        public static T ReadBody<T>(HttpRequestBase request)
        {
            request.InputStream.Position = 0;
            using (var reader = new StreamReader(request.InputStream))
            {
                var json = reader.ReadToEnd();
                if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Cuerpo vacío.");
                return JsonConvert.DeserializeObject<T>(json, Settings);
            }
        }
    }
}
