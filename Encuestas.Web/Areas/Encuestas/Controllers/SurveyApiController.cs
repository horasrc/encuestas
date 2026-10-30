using System;
using System.Collections.Generic;
using System.Web;
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
    /// El módulo no tiene administración de usuarios: aplica lo que el portal indica en
    /// Infrastructure.SurveyAccess (tramo de control):
    ///   - Acciones de administración: 403 si SurveyScope.CanAdminister es false.
    ///   - Segmento (encabezado X-Survey-Segment): solo encuestas de ese segmento; las
    ///     nuevas quedan en él. 403 si el segmento no está en SurveyScope.Segments.
    ///   - Results: solo respuestas de SurveyScope.Team.
    /// Sin SurveyAccess.Resolver no se restringe nada.
    ///
    /// Administración:
    ///   GET  /Encuestas/Api/List                       -> SurveySummaryDto[]
    ///   GET  /Encuestas/Api/Survey/{surveyId}          -> SurveyDto (todas las versiones)
    ///   GET  /Encuestas/Api/History/{surveyId}         -> AuditEntryDto[] (bitácora, últimas 100)
    ///   GET  /Encuestas/Api/Results/{versionId}?surveyId=  -> ResultsDto
    ///   POST /Encuestas/Api/Create                     -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/Duplicate   { surveyId }   -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/Delete      { surveyId }   -> ApiResult&lt;string&gt; (solo sin respuestas)
    ///   POST /Encuestas/Api/SaveDraft                  -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/Publish                    -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/CreateVersion              -> ApiResult&lt;SurveyDto&gt;
    ///   POST /Encuestas/Api/ChangeStatus               -> ApiResult&lt;SurveyDto&gt;
    /// Quien responde:
    ///   GET  /Encuestas/Api/Pending                    -> SurveySummaryDto[] (publicadas del segmento que puede responder)
    ///   GET  /Encuestas/Api/Published/{surveyId}       -> SurveyDto (solo la versión publicada)
    ///   GET  /Encuestas/Api/Eligibility/{surveyId}     -> EligibilityDto (¿puede responder ahora?)
    ///   POST /Encuestas/Api/Submit                     -> ApiResult&lt;string&gt;
    ///   POST /Encuestas/Api/LastResponse { surveyId, value } -> LastResponseDto (última captura con ese identificador)
    /// </summary>
    public class SurveyApiController : Controller
    {
        /// <summary>Acciones que solo puede usar quien administra (SurveyScope.CanAdminister).</summary>
        private static readonly HashSet<string> AdminActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "List", "Survey", "History", "Results", "Create", "Duplicate", "Delete", "SaveDraft", "Publish", "CreateVersion", "ChangeStatus"
        };

        private SurveyService _service;

        /// <summary>Se crea por petición con el usuario, el segmento y el tramo de control que indica el portal.</summary>
        private SurveyService Service
        {
            get
            {
                if (_service != null) return _service;
                var scope = SurveyAccess.Current(HttpContext);
                return _service = SurveyServiceFactory.Create(new SurveyServiceOptions
                {
                    UserName = CurrentUserName(),
                    Segment = SurveyAccess.CurrentSegment(HttpContext),
                    AllowedSegments = scope.Segments,
                    Team = scope.Team
                });
            }
        }

        /// <summary>Aplica el alcance del portal antes de cada acción (respuesta JSON 403 si no le corresponde).</summary>
        protected override void OnActionExecuting(ActionExecutingContext filterContext)
        {
            base.OnActionExecuting(filterContext);
            if (filterContext.Result != null) return;
            var action = filterContext.ActionDescriptor.ActionName;
            if (AdminActions.Contains(action) && !SurveyAccess.Current(HttpContext).CanAdminister)
            {
                filterContext.Result = new JsonNetResult(new { message = "No tienes permiso para administrar encuestas." }, 403);
                return;
            }
            try
            {
                SurveyAccess.CurrentSegment(HttpContext);
            }
            catch (HttpException ex)
            {
                filterContext.Result = new JsonNetResult(new { message = ex.Message }, ex.GetHttpCode());
            }
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
        public ActionResult Pending()
        {
            return new JsonNetResult(Service.GetPending(CurrentUserName()));
        }

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
