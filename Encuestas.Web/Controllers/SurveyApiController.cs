using System.Web.Mvc;
using Encuestas.Web.Infrastructure;
using Encuestas.Web.Models.Dto;
using Encuestas.Web.Services;

namespace Encuestas.Web.Controllers
{
    /// <summary>
    /// API JSON que consume Scripts/survey/survey-api.js cuando
    /// Surveys:UseMockData = false. Rutas: /SurveyApi/{accion}/{id}.
    ///
    /// Administración ([SurveyAdminAuthorize]):
    ///   GET  /SurveyApi/List                       -> SurveySummaryDto[]
    ///   GET  /SurveyApi/Survey/{surveyId}          -> SurveyDto (todas las versiones)
    ///   GET  /SurveyApi/Catalogs                   -> OrgCatalogs
    ///   GET  /SurveyApi/Results/{versionId}?surveyId=  -> ResultsDto
    ///   POST /SurveyApi/Create                     -> ApiResult&lt;SurveyDto&gt;
    ///   POST /SurveyApi/Duplicate   { surveyId }   -> ApiResult&lt;SurveyDto&gt;
    ///   POST /SurveyApi/Delete      { surveyId }   -> ApiResult&lt;string&gt; (solo sin respuestas)
    ///   POST /SurveyApi/SaveDraft                  -> ApiResult&lt;SurveyDto&gt;
    ///   POST /SurveyApi/Publish                    -> ApiResult&lt;SurveyDto&gt;
    ///   POST /SurveyApi/CreateVersion              -> ApiResult&lt;SurveyDto&gt;
    ///   POST /SurveyApi/ChangeStatus               -> ApiResult&lt;SurveyDto&gt;
    /// Quien responde:
    ///   GET  /SurveyApi/Published/{surveyId}       -> SurveyDto (solo la versión publicada)
    ///   GET  /SurveyApi/Eligibility/{surveyId}     -> EligibilityDto (¿puede responder ahora?)
    ///   POST /SurveyApi/Submit                     -> ApiResult&lt;string&gt;
    ///
    /// Todos los POST exigen el encabezado RequestVerificationToken.
    /// </summary>
    [ValidateJsonAntiForgeryToken]
    public class SurveyApiController : Controller
    {
        private readonly SurveyService _service = SurveyServiceFactory.Create();

        // -----------------------------------------------------------------
        // Administración
        // -----------------------------------------------------------------

        [HttpGet, SurveyAdminAuthorize]
        public ActionResult List()
        {
            return new JsonNetResult(_service.GetSurveyList());
        }

        [HttpGet, SurveyAdminAuthorize]
        public ActionResult Survey(string id)
        {
            var survey = _service.GetSurvey(id);
            return survey == null ? NotFound("Encuesta no encontrada") : new JsonNetResult(survey);
        }

        [HttpGet, SurveyAdminAuthorize]
        public ActionResult Catalogs()
        {
            return new JsonNetResult(_service.GetCatalogs());
        }

        [HttpGet, SurveyAdminAuthorize]
        public ActionResult Results(string id, string surveyId)
        {
            var results = _service.GetResults(surveyId, id);
            return results == null ? NotFound("Versión no encontrada") : new JsonNetResult(results);
        }

        [HttpPost, SurveyAdminAuthorize]
        public ActionResult Create()
        {
            return Reply(_service.CreateSurvey(JsonNetResult.ReadBody<CreateSurveyRequest>(Request)));
        }

        [HttpPost, SurveyAdminAuthorize]
        public ActionResult Duplicate()
        {
            return Reply(_service.DuplicateSurvey(JsonNetResult.ReadBody<VersionActionRequest>(Request).SurveyId));
        }

        [HttpPost, SurveyAdminAuthorize]
        public ActionResult Delete()
        {
            return Reply(_service.DeleteSurvey(JsonNetResult.ReadBody<VersionActionRequest>(Request).SurveyId));
        }

        [HttpPost, SurveyAdminAuthorize]
        public ActionResult SaveDraft()
        {
            return Reply(_service.SaveDraft(JsonNetResult.ReadBody<SaveDraftRequest>(Request)));
        }

        [HttpPost, SurveyAdminAuthorize]
        public ActionResult Publish()
        {
            return Reply(_service.Publish(JsonNetResult.ReadBody<VersionActionRequest>(Request)));
        }

        [HttpPost, SurveyAdminAuthorize]
        public ActionResult CreateVersion()
        {
            return Reply(_service.CreateVersion(JsonNetResult.ReadBody<VersionActionRequest>(Request)));
        }

        [HttpPost, SurveyAdminAuthorize]
        public ActionResult ChangeStatus()
        {
            return Reply(_service.ChangeStatus(JsonNetResult.ReadBody<VersionActionRequest>(Request)));
        }

        // -----------------------------------------------------------------
        // Colaborador que responde
        // -----------------------------------------------------------------

        [HttpGet]
        public ActionResult Published(string id)
        {
            var survey = _service.GetPublished(id);
            return survey == null ? NotFound("Encuesta no encontrada") : new JsonNetResult(survey);
        }

        [HttpGet]
        public ActionResult Eligibility(string id)
        {
            return new JsonNetResult(_service.GetEligibility(id, CurrentUserName()));
        }

        [HttpPost]
        public ActionResult Submit()
        {
            return Reply(_service.Submit(JsonNetResult.ReadBody<SubmitResponseDto>(Request), CurrentUserName()));
        }

        private string CurrentUserName()
        {
            return User != null && User.Identity.IsAuthenticated ? User.Identity.Name : null;
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
