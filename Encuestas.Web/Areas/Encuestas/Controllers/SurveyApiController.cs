using System.Web.Mvc;
using ModuloEncuestas.Infrastructure;
using ModuloEncuestas.Models.Dto;
using ModuloEncuestas.Services;

namespace ModuloEncuestas.Controllers
{
    /// <summary>
    /// API JSON que consume Scripts/survey/survey-api.js cuando
    /// Surveys:UseMockData = false. Rutas: /Encuestas/Api/{accion}/{id}.
    ///
    /// El acceso lo controla el portal (el módulo no restringe por usuario ni rol).
    /// Para que solo ciertas personas administren, el portal debe proteger /Encuestas,
    /// /Encuestas/Constructor y estas rutas de administración; /Encuestas/Responder,
    /// Published, Eligibility, Submit y LastResponse son para quien responde.
    ///
    /// Administración:
    ///   GET  /Encuestas/Api/List                       -> SurveySummaryDto[]
    ///   GET  /Encuestas/Api/Survey/{surveyId}          -> SurveyDto (todas las versiones)
    ///   GET  /Encuestas/Api/History/{surveyId}         -> AuditEntryDto[] (bitácora, últimas 100)
    ///   GET  /Encuestas/Api/Catalogs                   -> OrgCatalogs
    ///   GET  /Encuestas/Api/Results/{versionId}?surveyId=  -> ResultsDto
    ///   POST /Encuestas/Api/Create                     -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/Duplicate   { surveyId }   -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/Delete      { surveyId }   -> ApiResult&lt;string&gt; (solo sin respuestas)
    ///   POST /Encuestas/Api/SaveDraft                  -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/Publish                    -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/CreateVersion              -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/ChangeStatus               -> ApiResult&lt;SurveyDto&gt;
    /// Quien responde:
    ///   GET  /Encuestas/Api/Published/{surveyId}       -> SurveyDto (solo la versión publicada)
    ///   GET  /Encuestas/Api/Eligibility/{surveyId}     -> EligibilityDto (¿puede responder ahora?)
    ///   POST /Encuestas/Api/Submit                     -> ApiResult&lt;string&gt;
    ///   POST /Encuestas/Api/LastResponse { surveyId, value } -> LastResponseDto (última captura con ese identificador)
    ///
    /// Todos los POST exigen el encabezado RequestVerificationToken.
    /// </summary>
    [ValidateJsonAntiForgeryToken]
    public class SurveyApiController : Controller
    {
        private SurveyService _service;

        /// <summary>Se crea por petición con el usuario actual (lo usa la bitácora).</summary>
        private SurveyService Service
        {
            get { return _service ?? (_service = SurveyServiceFactory.Create(CurrentUserName())); }
        }

        // -----------------------------------------------------------------
        // Administración
        // -----------------------------------------------------------------

        [HttpGet]
        public ActionResult List()
        {
            return new JsonNetResult(Service.GetSurveyList());
        }

        [HttpGet]
        public ActionResult Survey(string id)
        {
            var survey = Service.GetSurvey(id);
            return survey == null ? NotFound("Encuesta no encontrada") : new JsonNetResult(survey);
        }

        [HttpGet]
        public ActionResult History(string id)
        {
            return new JsonNetResult(Service.GetHistory(id));
        }

        [HttpGet]
        public ActionResult Catalogs()
        {
            return new JsonNetResult(Service.GetCatalogs());
        }

        [HttpGet]
        public ActionResult Results(string id, string surveyId)
        {
            var results = Service.GetResults(surveyId, id);
            return results == null ? NotFound("Versión no encontrada") : new JsonNetResult(results);
        }

        [HttpPost]
        public ActionResult Create()
        {
            return Reply(Service.CreateSurvey(JsonNetResult.ReadBody<CreateSurveyRequest>(Request)));
        }

        [HttpPost]
        public ActionResult Duplicate()
        {
            return Reply(Service.DuplicateSurvey(JsonNetResult.ReadBody<VersionActionRequest>(Request).SurveyId));
        }

        [HttpPost]
        public ActionResult Delete()
        {
            return Reply(Service.DeleteSurvey(JsonNetResult.ReadBody<VersionActionRequest>(Request).SurveyId));
        }

        [HttpPost]
        public ActionResult SaveDraft()
        {
            return Reply(Service.SaveDraft(JsonNetResult.ReadBody<SaveDraftRequest>(Request)));
        }

        [HttpPost]
        public ActionResult Publish()
        {
            return Reply(Service.Publish(JsonNetResult.ReadBody<VersionActionRequest>(Request)));
        }

        [HttpPost]
        public ActionResult CreateVersion()
        {
            return Reply(Service.CreateVersion(JsonNetResult.ReadBody<VersionActionRequest>(Request)));
        }

        [HttpPost]
        public ActionResult ChangeStatus()
        {
            return Reply(Service.ChangeStatus(JsonNetResult.ReadBody<VersionActionRequest>(Request)));
        }

        // -----------------------------------------------------------------
        // Colaborador que responde
        // -----------------------------------------------------------------

        [HttpGet]
        public ActionResult Published(string id)
        {
            var survey = Service.GetPublished(id);
            return survey == null ? NotFound("Encuesta no encontrada") : new JsonNetResult(survey);
        }

        [HttpGet]
        public ActionResult Eligibility(string id)
        {
            return new JsonNetResult(Service.GetEligibility(id, CurrentUserName()));
        }

        [HttpPost]
        public ActionResult Submit()
        {
            return Reply(Service.Submit(JsonNetResult.ReadBody<SubmitResponseDto>(Request), CurrentUserName()));
        }

        [HttpPost]
        public ActionResult LastResponse()
        {
            var request = JsonNetResult.ReadBody<LastResponseRequest>(Request) ?? new LastResponseRequest();
            return new JsonNetResult(Service.GetLastResponse(request.SurveyId, request.Value, CurrentUserName()));
        }

        private string CurrentUserName()
        {
            return SurveyUser.Current(HttpContext);
        }

        private static ActionResult NotFound(string message)
        {
            return new JsonNetResult(new { message = message }, 404);
        }

        private static ActionResult Reply<T>(ApiResult<T> result)
        {
            return new JsonNetResult(result, result.Ok ? 200 : 400);
        }
    }
}
