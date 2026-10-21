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
        IList<SurveyDto> GetSurveys();
        SurveyDto GetSurvey(string surveyId);

        /// <summary>Inserta o actualiza el agregado completo (encuesta + versiones).</summary>
        void SaveSurvey(SurveyDto survey);

        /// <summary>Baja lógica (IsDeleted). El servicio solo la permite si no hay respuestas.</summary>
        void DeleteSurvey(string surveyId);

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

        /// <summary>Catálogos organizacionales para segmentación (roles, segmentos, regiones, sucursales, usuarios).</summary>
        OrgCatalogs GetCatalogs();

        /// <summary>Registra una acción en la bitácora (SurveyAuditLog).</summary>
        void AddAudit(AuditEntryDto entry);

        /// <summary>Últimas entradas de la bitácora de una encuesta, de la más reciente a la más antigua.</summary>
        IList<AuditEntryDto> GetAudit(string surveyId, int max);
    }

    /// <summary>
    /// Origen de los catálogos de segmentación. Separado del repositorio de encuestas
    /// porque normalmente vienen de otro sistema (RH, Active Directory, data warehouse).
    /// </summary>
    public interface IOrgCatalogProvider
    {
        OrgCatalogs GetCatalogs();
    }

    /// <summary>Catálogos para la segmentación. Normalmente provienen del directorio de RH / AD.</summary>
    public class OrgCatalogs
    {
        public List<CatalogItem> Roles { get; set; }
        public List<CatalogItem> Segments { get; set; }
        public List<CatalogItem> Regions { get; set; }
        public List<BranchItem> Branches { get; set; }
        public List<UserItem> Users { get; set; }

        public OrgCatalogs()
        {
            Roles = new List<CatalogItem>();
            Segments = new List<CatalogItem>();
            Regions = new List<CatalogItem>();
            Branches = new List<BranchItem>();
            Users = new List<UserItem>();
        }
    }

    public class CatalogItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }

    public class BranchItem : CatalogItem
    {
        public string RegionId { get; set; }
        public string SegmentId { get; set; }

        /// <summary>Plantilla por rol (roleId -> personas). Sirve para estimar la audiencia.</summary>
        public Dictionary<string, int> Headcount { get; set; }
    }

    public class UserItem : CatalogItem
    {
        /// <summary>Usuario de red (ej. "DOMINIO\\usuario"); se compara con User.Identity.Name al validar la audiencia.</summary>
        public string Login { get; set; }
        public string Email { get; set; }
        public string RoleId { get; set; }
        public string BranchId { get; set; }
    }
}
