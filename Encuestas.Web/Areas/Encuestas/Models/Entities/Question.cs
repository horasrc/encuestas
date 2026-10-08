using System.Collections.Generic;

namespace ModuloEncuestas.Models.Entities
{
    /// <summary>
    /// Catálogo de tipos de pregunta (tabla QuestionType).
    /// Code coincide con el "type" que usa el front-end (SurveyTypes en survey-types.js).
    /// </summary>
    public class QuestionType
    {
        public int QuestionTypeId { get; set; }
        public string Code { get; set; }   // singleChoice, multipleChoice, text, yesNo, scale, date, number, dropdown, matrix, dependent
        public string Name { get; set; }
        public string Icon { get; set; }
    }

    /// <summary>
    /// Pregunta perteneciente a una versión concreta de la encuesta.
    /// Las validaciones específicas por tipo (min/max caracteres, selecciones, rango,
    /// escala...) se guardan en SettingsJson para no agregar columnas por cada tipo nuevo.
    /// </summary>
    public class Question
    {
        public int QuestionId { get; set; }
        public int SurveyVersionId { get; set; }
        public int QuestionTypeId { get; set; }

        /// <summary>Identificador estable del cliente (ej. "q_k3j2"), útil al mapear reglas antes de tener IDs de BD.</summary>
        public string ClientKey { get; set; }

        public int SortOrder { get; set; }
        public string Text { get; set; }
        public string HelpText { get; set; }
        public bool IsRequired { get; set; }
        public bool RandomizeOptions { get; set; }
        public bool AllowOther { get; set; }
        public string OtherLabel { get; set; }

        /// <summary>JSON con QuestionValidationDto (minLength, maxLength, minSelections, ...).</summary>
        public string SettingsJson { get; set; }

        public virtual QuestionType QuestionType { get; set; }
        public virtual ICollection<QuestionOption> Options { get; set; }
        public virtual ICollection<DependentLevel> DependentLevels { get; set; }
        public virtual ICollection<DependentOption> DependentOptions { get; set; }
        public virtual ICollection<ConditionalRule> Rules { get; set; }

        public Question()
        {
            Options = new List<QuestionOption>();
            DependentLevels = new List<DependentLevel>();
            DependentOptions = new List<DependentOption>();
            Rules = new List<ConditionalRule>();
        }
    }

    /// <summary>Rol de una opción: respuesta normal, fila de matriz o columna de matriz.</summary>
    public enum OptionRole : byte
    {
        Choice = 1,
        MatrixRow = 2,
        MatrixColumn = 3
    }

    /// <summary>
    /// Opción de respuesta. Se reutiliza para filas y columnas de la matriz (Role).
    /// </summary>
    public class QuestionOption
    {
        public int QuestionOptionId { get; set; }
        public int QuestionId { get; set; }
        public OptionRole Role { get; set; }
        public string ClientKey { get; set; }
        public string Text { get; set; }
        public string Value { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>Nivel de una lista dependiente (ej. 0 = Banca, 1 = Producto, 2 = Recurso).</summary>
    public class DependentLevel
    {
        public int DependentLevelId { get; set; }
        public int QuestionId { get; set; }
        public int LevelIndex { get; set; }
        public string Name { get; set; }
    }

    /// <summary>
    /// Opción de lista encadenada. Forma un árbol mediante ParentOptionId:
    /// Banca Personal (nivel 0) -> Tarjeta de crédito (nivel 1) -> Guía comercial (nivel 2).
    /// </summary>
    public class DependentOption
    {
        public int DependentOptionId { get; set; }
        public int QuestionId { get; set; }
        public int? ParentOptionId { get; set; }
        public int LevelIndex { get; set; }
        public string ClientKey { get; set; }
        public string Text { get; set; }
        public int SortOrder { get; set; }
    }

    /// <summary>Operadores soportados por las reglas condicionales.</summary>
    public static class RuleOperators
    {
        public const string EqualTo = "equals";       // la respuesta incluye la opción X
        public const string NotEqualTo = "notEquals"; // la respuesta no incluye la opción X
        public const string GreaterThan = "gt";
        public const string LessThan = "lt";
        public const string Contains = "contains";    // texto contiene
        public const string Answered = "answered";    // la pregunta fue respondida
    }

    /// <summary>Acciones de una regla condicional.</summary>
    public static class RuleActions
    {
        public const string Show = "show";   // Mostrar pregunta Y (Y queda oculta por defecto)
        public const string Hide = "hide";   // Ocultar pregunta Y
        public const string GoTo = "goto";   // Saltar hasta la pregunta Z
        public const string End = "end";     // Finalizar encuesta
    }

    /// <summary>
    /// Regla: SI (respuesta de QuestionId) [Operator] [CompareValue] ENTONCES [Action] [TargetQuestionId].
    /// </summary>
    public class ConditionalRule
    {
        public int ConditionalRuleId { get; set; }
        public int QuestionId { get; set; }
        public string Operator { get; set; }

        /// <summary>Valor a comparar: ClientKey de opción, número o texto según el tipo.</summary>
        public string CompareValue { get; set; }

        public string Action { get; set; }
        public int? TargetQuestionId { get; set; }
        public int SortOrder { get; set; }
    }
}
