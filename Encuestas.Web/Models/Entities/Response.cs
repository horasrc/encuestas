using System;
using System.Collections.Generic;

namespace Encuestas.Web.Models.Entities
{
    /// <summary>
    /// Una respuesta completa (un usuario contestando una versión).
    /// Se liga a la versión, no a la encuesta, para que los resultados
    /// siempre correspondan a las preguntas que vio el usuario.
    /// </summary>
    public class SurveyResponse
    {
        public long SurveyResponseId { get; set; }
        public int SurveyVersionId { get; set; }

        /// <summary>Null cuando la encuesta es anónima.</summary>
        public string UserId { get; set; }

        /// <summary>Hash del usuario para controlar "una respuesta por usuario" aun siendo anónima.</summary>
        public string UserHash { get; set; }

        public DateTime StartedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }

        public virtual ICollection<QuestionResponse> Answers { get; set; }

        public SurveyResponse()
        {
            Answers = new List<QuestionResponse>();
        }
    }

    /// <summary>
    /// Modelo "EAV" de respuestas: una fila por valor respondido.
    /// - Opción única / Sí-No / Desplegable: 1 fila con QuestionOptionId.
    /// - Selección múltiple: 1 fila por opción marcada; "Otro" = fila con TextValue y QuestionOptionId NULL.
    /// - Texto: TextValue. Número / Escala: NumberValue. Fecha: DateValue.
    /// - Matriz: 1 fila por celda marcada (MatrixRowOptionId + QuestionOptionId de la columna).
    /// - Dependiente: 1 fila por nodo seleccionado (DependentOptionId).
    /// Así nunca se crean columnas pregunta1, pregunta2, ...
    /// </summary>
    public class QuestionResponse
    {
        public long QuestionResponseId { get; set; }
        public long SurveyResponseId { get; set; }
        public int QuestionId { get; set; }

        public int? QuestionOptionId { get; set; }
        public int? MatrixRowOptionId { get; set; }
        public int? DependentOptionId { get; set; }

        public string TextValue { get; set; }
        public decimal? NumberValue { get; set; }
        public DateTime? DateValue { get; set; }
    }
}
