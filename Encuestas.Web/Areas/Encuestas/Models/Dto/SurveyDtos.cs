using System;
using System.Collections.Generic;

namespace ModuloEncuestas.Models.Dto
{
    // =====================================================================
    // CONTRATO JSON ENTRE FRONT-END Y BACK-END
    // ---------------------------------------------------------------------
    // Estas clases reflejan exactamente la forma de los objetos que maneja
    // el JavaScript (ver Scripts/survey/mock-data.js). Se serializan en
    // camelCase con JsonNetResult. Los IDs son string para permitir IDs
    // temporales generados en el navegador ("q_k3j2"); el repositorio SQL
    // los traduce a IDs enteros (columna ClientKey).
    // =====================================================================

    public class SurveyDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public List<SurveyVersionDto> Versions { get; set; }

        public SurveyDto()
        {
            Versions = new List<SurveyVersionDto>();
        }
    }

    public class SurveyVersionDto
    {
        public string Id { get; set; }
        public int Number { get; set; }

        /// <summary>Draft | Published | Closed | Archived</summary>
        public string Status { get; set; }

        public int ResponseCount { get; set; }
        public DateTime? CreatedAt { get; set; }
        public DateTime? PublishedAt { get; set; }

        public PublishSettingsDto Settings { get; set; }
        public SegmentationDto Segmentation { get; set; }
        public List<QuestionDto> Questions { get; set; }

        public SurveyVersionDto()
        {
            Settings = new PublishSettingsDto();
            Segmentation = new SegmentationDto();
            Questions = new List<QuestionDto>();
        }
    }

    public class PublishSettingsDto
    {
        public string StartDate { get; set; }   // yyyy-MM-dd
        public string EndDate { get; set; }     // yyyy-MM-dd
        public bool AllowMultipleResponses { get; set; }
        public bool Anonymous { get; set; }
        public bool RequireAuth { get; set; }
        public string WelcomeMessage { get; set; }
        public string ThankYouMessage { get; set; }
    }

    /// <summary>
    /// Segmentos del portal a los que pertenece la encuesta (ej. el "segmento" de RadarController).
    /// Los asigna el servidor al crearla, según el segmento en que se trabaja; no se edita en pantalla.
    /// Sin segmentos = la encuesta aparece en cualquier segmento.
    /// </summary>
    public class SegmentationDto
    {
        /// <summary>all | segmented</summary>
        public string Mode { get; set; }
        public List<string> Segments { get; set; }

        public SegmentationDto()
        {
            Mode = "all";
            Segments = new List<string>();
        }

        public static SegmentationDto For(string segment)
        {
            var s = new SegmentationDto();
            if (!string.IsNullOrWhiteSpace(segment))
            {
                s.Mode = "segmented";
                s.Segments.Add(segment.Trim());
            }
            return s;
        }

        /// <summary>¿La encuesta aparece en ese segmento? Null = no se trabaja por segmento.</summary>
        public bool Includes(string segment)
        {
            if (segment == null || Mode != "segmented" || Segments == null || Segments.Count == 0) return true;
            return Segments.Exists(s => string.Equals(s, segment, StringComparison.OrdinalIgnoreCase));
        }
    }

    public class QuestionDto
    {
        public string Id { get; set; }
        public int Order { get; set; }
        public string Type { get; set; }
        public string Text { get; set; }
        public string HelpText { get; set; }
        public bool Required { get; set; }
        public bool RandomizeOptions { get; set; }
        public bool AllowOther { get; set; }
        public string OtherLabel { get; set; }

        /// <summary>
        /// Desplegable en cascada: Id de la pregunta (anterior, de opciones) de la que dependen
        /// sus opciones. Cada opción indica en OptionDto.ParentId con qué opción de esa pregunta aparece.
        /// </summary>
        public string ParentQuestionId { get; set; }

        /// <summary>
        /// Texto que identifica lo capturado (ej. número de cliente). Con él se busca la última
        /// respuesta de la encuesta que tenga ese valor para precargarla.
        /// </summary>
        public bool Identifier { get; set; }

        /// <summary>
        /// Pregunta de fecha: al enviar, quien responde puede descargar la cita (.ics) para Outlook.
        /// </summary>
        public bool Outlook { get; set; }

        /// <summary>Asunto de la cita en Outlook; vacío = texto de la pregunta.</summary>
        public string OutlookSubject { get; set; }

        public QuestionValidationDto Validation { get; set; }
        public List<OptionDto> Options { get; set; }
        public MatrixDto Matrix { get; set; }
        public DependentDto Dependent { get; set; }
        public List<RuleDto> Rules { get; set; }

        public QuestionDto()
        {
            Validation = new QuestionValidationDto();
            Options = new List<OptionDto>();
            Rules = new List<RuleDto>();
        }
    }

    /// <summary>Se guarda completo en Question.SettingsJson.</summary>
    public class QuestionValidationDto
    {
        public int? MinLength { get; set; }
        public int? MaxLength { get; set; }
        public int? MinSelections { get; set; }
        public int? MaxSelections { get; set; }
        public decimal? MinValue { get; set; }
        public decimal? MaxValue { get; set; }
        public string TextMode { get; set; }       // short | long
        public int? ScaleMin { get; set; }
        public int? ScaleMax { get; set; }
        public string ScaleMinLabel { get; set; }
        public string ScaleMaxLabel { get; set; }

        /// <summary>Copia superficial (todas las propiedades son valores o strings).</summary>
        public QuestionValidationDto Copy()
        {
            return (QuestionValidationDto)MemberwiseClone();
        }
    }

    public class OptionDto
    {
        public string Id { get; set; }
        public string Text { get; set; }

        /// <summary>Desplegable en cascada: opción de la pregunta padre con la que aparece (null = siempre).</summary>
        public string ParentId { get; set; }
    }

    public class MatrixDto
    {
        /// <summary>single (una respuesta por fila) | multiple</summary>
        public string SelectionType { get; set; }
        public List<OptionDto> Rows { get; set; }
        public List<OptionDto> Columns { get; set; }

        public MatrixDto()
        {
            SelectionType = "single";
            Rows = new List<OptionDto>();
            Columns = new List<OptionDto>();
        }
    }

    public class DependentDto
    {
        public bool Multiple { get; set; }
        public List<DependentLevelDto> Levels { get; set; }
        public List<DependentOptionDto> Options { get; set; }

        public DependentDto()
        {
            Levels = new List<DependentLevelDto>();
            Options = new List<DependentOptionDto>();
        }
    }

    public class DependentLevelDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class DependentOptionDto
    {
        public string Id { get; set; }
        public string ParentId { get; set; }
        public int Level { get; set; }
        public string Text { get; set; }
    }

    public class RuleDto
    {
        public string Id { get; set; }
        public string Operator { get; set; }
        public string Value { get; set; }
        public string Action { get; set; }
        public string TargetQuestionId { get; set; }
    }

    // ---------------------------------------------------------------------
    // Respuestas
    // ---------------------------------------------------------------------

    /// <summary>Respuesta a una pregunta tal como la envía el navegador.</summary>
    public class AnswerDto
    {
        public List<string> OptionIds { get; set; }
        public string OtherText { get; set; }
        public string Text { get; set; }
        public decimal? Number { get; set; }
        public string Date { get; set; }

        /// <summary>Fecha con cita para Outlook: hora opcional "HH:mm" (se guarda en TextValue de la fila de la fecha).</summary>
        public string Time { get; set; }

        /// <summary>Matriz: rowId -> columnas seleccionadas.</summary>
        public Dictionary<string, List<string>> Matrix { get; set; }
    }

    public class SubmitResponseDto
    {
        public string SurveyId { get; set; }
        public string VersionId { get; set; }
        public Dictionary<string, AnswerDto> Answers { get; set; }
    }

    public class ResponseRecordDto
    {
        public string Id { get; set; }
        public string SubmittedAt { get; set; }
        public Dictionary<string, AnswerDto> Answers { get; set; }

        /// <summary>SHA-256 de quien respondió (SurveyResponse.UserHash). Solo para filtrar por tramo; no se envía al navegador.</summary>
        [Newtonsoft.Json.JsonIgnore]
        public string UserHash { get; set; }
    }

    /// <summary>Búsqueda por identificador. Va en el cuerpo de un POST para que el valor no quede en las bitácoras de IIS.</summary>
    public class LastResponseRequest
    {
        public string SurveyId { get; set; }
        public string Value { get; set; }
    }

    /// <summary>Última respuesta capturada con un identificador (ej. número de cliente), lista para precargar.</summary>
    public class LastResponseDto
    {
        public bool Found { get; set; }
        public string Message { get; set; }
        public string SubmittedAt { get; set; }

        /// <summary>Número de la versión en que se capturó (puede ser anterior a la publicada).</summary>
        public int VersionNumber { get; set; }

        /// <summary>Respuestas expresadas con los IDs de la versión publicada.</summary>
        public Dictionary<string, AnswerDto> Answers { get; set; }

        public LastResponseDto()
        {
            Answers = new Dictionary<string, AnswerDto>();
        }
    }

    public class ResultsDto
    {
        public string VersionId { get; set; }

        /// <summary>Personas del tramo de control (0 = el portal no lo indica).</summary>
        public int Audience { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
        public List<ResponseRecordDto> Responses { get; set; }

        public ResultsDto()
        {
            Responses = new List<ResponseRecordDto>();
        }
    }

    // ---------------------------------------------------------------------
    // Peticiones de la API
    // ---------------------------------------------------------------------

    public class SaveDraftRequest
    {
        public string SurveyId { get; set; }
        public string Name { get; set; }
        public SurveyVersionDto Version { get; set; }
    }

    /// <summary>Si el usuario puede responder la versión publicada (y por qué no).</summary>
    public class EligibilityDto
    {
        public bool CanRespond { get; set; }

        /// <summary>unavailable | notStarted | ended | authRequired | alreadyResponded | notInSegment</summary>
        public string Code { get; set; }
        public string Reason { get; set; }
        public string VersionId { get; set; }

        public static EligibilityDto Denied(string code, string reason)
        {
            return new EligibilityDto { CanRespond = false, Code = code, Reason = reason };
        }
    }

    /// <summary>Entrada de la bitácora de cambios.</summary>
    public class AuditEntryDto
    {
        public string SurveyId { get; set; }
        public int? VersionNumber { get; set; }

        /// <summary>created | saved | newVersion | published | archived | closed | duplicated | deleted</summary>
        public string Action { get; set; }
        public string Detail { get; set; }
        public string UserName { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class CreateSurveyRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
    }

    /// <summary>Fila del listado de encuestas (pantalla /Encuestas).</summary>
    public class SurveySummaryDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        /// <summary>Estado de la versión vigente: la publicada; si no hay, el borrador; si no, la última.</summary>
        public string Status { get; set; }
        public int CurrentVersion { get; set; }

        /// <summary>Número del borrador en edición junto a una versión publicada (null si no hay).</summary>
        public int? DraftVersion { get; set; }

        public int ResponseCount { get; set; }
        public int TotalResponses { get; set; }
        public int VersionCount { get; set; }
        public string StartDate { get; set; }
        public string EndDate { get; set; }
    }

    public class VersionActionRequest
    {
        public string SurveyId { get; set; }
        public string VersionId { get; set; }

        /// <summary>Solo para ChangeStatus: Closed | Archived.</summary>
        public string Status { get; set; }
    }

    /// <summary>Resultado estándar de las operaciones de escritura.</summary>
    public class ApiResult<T>
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public List<string> Errors { get; set; }
        public T Data { get; set; }

        public static ApiResult<T> Success(T data, string message)
        {
            return new ApiResult<T> { Ok = true, Data = data, Message = message, Errors = new List<string>() };
        }

        public static ApiResult<T> Fail(string message, List<string> errors)
        {
            return new ApiResult<T> { Ok = false, Message = message, Errors = errors ?? new List<string>() };
        }
    }
}
