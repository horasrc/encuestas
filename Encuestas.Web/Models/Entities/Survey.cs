using System;
using System.Collections.Generic;

namespace Encuestas.Web.Models.Entities
{
    /// <summary>
    /// Estados del ciclo de vida de una versión de encuesta.
    /// Se guardan como TINYINT en SurveyVersion.Status.
    /// </summary>
    public enum SurveyStatus : byte
    {
        Draft = 1,      // Borrador: editable.
        Published = 2,  // Publicada: recibe respuestas. Si ya tiene respuestas NO se sobrescribe.
        Closed = 3,     // Cerrada: ya no recibe respuestas, conserva resultados.
        Archived = 4    // Archivada: histórico (p. ej. reemplazada por una versión más nueva).
    }

    /// <summary>
    /// Encuesta "contenedora". El contenido real (preguntas, configuración)
    /// vive en SurveyVersion para poder versionar sin perder histórico.
    /// </summary>
    public class Survey
    {
        public int SurveyId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool IsDeleted { get; set; }

        public virtual ICollection<SurveyVersion> Versions { get; set; }

        public Survey()
        {
            Versions = new List<SurveyVersion>();
        }
    }

    /// <summary>
    /// Una versión inmutable una vez que tiene respuestas.
    /// Regla: una versión Published con ResponseCount &gt; 0 nunca se modifica;
    /// cualquier cambio genera una nueva versión Draft (ver SurveyService).
    /// </summary>
    public class SurveyVersion
    {
        public int SurveyVersionId { get; set; }
        public int SurveyId { get; set; }
        public int VersionNumber { get; set; }
        public SurveyStatus Status { get; set; }

        // --- Publicación ---
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool AllowMultipleResponses { get; set; }
        public bool IsAnonymous { get; set; }
        public bool RequiresAuthentication { get; set; }
        public string WelcomeMessage { get; set; }
        public string ThankYouMessage { get; set; }

        // --- Auditoría ---
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; }
        public DateTime? PublishedAt { get; set; }
        public DateTime? ClosedAt { get; set; }

        /// <summary>Contador desnormalizado para no contar SurveyResponse en cada consulta.</summary>
        public int ResponseCount { get; set; }

        public virtual Survey Survey { get; set; }
        public virtual ICollection<Question> Questions { get; set; }
        public virtual ICollection<SurveyAudienceRule> AudienceRules { get; set; }

        public SurveyVersion()
        {
            Questions = new List<Question>();
            AudienceRules = new List<SurveyAudienceRule>();
        }
    }

    /// <summary>
    /// Segmentación: quién puede responder. Una fila por valor seleccionado.
    /// Semántica: OR dentro de la misma dimensión, AND entre dimensiones;
    /// los usuarios específicos (Dimension = User) siempre se incluyen.
    /// </summary>
    public class SurveyAudienceRule
    {
        public int AudienceRuleId { get; set; }
        public int SurveyVersionId { get; set; }

        /// <summary>Role | Segment | Region | Branch | User</summary>
        public string Dimension { get; set; }
        public string Value { get; set; }
    }
}
