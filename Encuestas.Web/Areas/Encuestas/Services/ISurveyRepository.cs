using System.Collections.Generic;
using ModuloEncuestas.Models.Dto;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Punto de conexión con la base de datos.
    /// Hoy lo implementa InMemorySurveyRepository (demo). Para producción crear
    /// SqlSurveyRepository (ADO.NET / Dapper / EF) que mapee estos DTOs a las
    /// tablas de Database/schema.sql y registrarlo en SurveyServiceFactory.
    /// </summary>
    public interface ISurveyRepository
    {
        /// <summary>Encuestas con sus versiones (sin preguntas) y los segmentos de cada versión.</summary>
        IList<SurveyDto> GetSurveys();
        SurveyDto GetSurvey(string surveyId);

        /// <summary>Inserta o actualiza el agregado completo (encuesta + versiones).</summary>
        void SaveSurvey(SurveyDto survey);

        /// <summary>Baja lógica (IsDeleted). El servicio solo la permite si no hay respuestas.</summary>
        void DeleteSurvey(string surveyId);

        /// <summary>Respuestas enviadas de la versión, cada una con su UserHash (para filtrar por tramo).</summary>
        IList<ResponseRecordDto> GetResponses(string versionId);

        /// <summary>Guarda la respuesta e incrementa ResponseCount de la versión (de forma atómica).</summary>
        void AddResponse(string versionId, ResponseRecordDto response, string userHash);
        bool HasUserResponded(string versionId, string userHash);

        /// <summary>
        /// Respuesta más reciente de la encuesta (cualquier versión) en la que alguna de las preguntas
        /// <paramref name="questionIds"/> tiene como texto <paramref name="value"/> (sin distinguir
        /// mayúsculas ni espacios al inicio/fin). Null si no hay; <paramref name="versionId"/> = versión de la respuesta.
        /// </summary>
        ResponseRecordDto FindLatestResponse(string surveyId, IList<string> questionIds, string value, out string versionId);

        /// <summary>Registra una acción en la bitácora (SurveyAuditLog).</summary>
        void AddAudit(AuditEntryDto entry);

        /// <summary>Últimas entradas de la bitácora de una encuesta, de la más reciente a la más antigua.</summary>
        IList<AuditEntryDto> GetAudit(string surveyId, int max);
    }
}
