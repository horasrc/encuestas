using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ModuloEncuestas.Models.Dto;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Puerto a C# de la lógica condicional (Scripts/survey/survey-logic.js) y de las
    /// validaciones por tipo (SurveyRenderer.validate en survey-renderer.js).
    /// Si se cambia la semántica en un lado, hay que cambiarla en el otro.
    ///
    /// Se usa al recibir respuestas: el servidor recalcula qué preguntas vio el
    /// usuario, exige solo las obligatorias visibles y descarta respuestas de
    /// preguntas ocultas o con opciones inexistentes.
    /// </summary>
    public static class SurveyLogic
    {
        public const string OtherOption = "__other__";

        /// <summary>option | number | text | answered (igual que SurveyTypes.ruleKind en JS).</summary>
        public static string RuleKind(string type)
        {
            switch (type)
            {
                case "singleChoice":
                case "multipleChoice":
                case "dropdown":
                case "yesNo":
                case "dependent":
                    return "option";
                case "scale":
                case "number":
                    return "number";
                case "text":
                    return "text";
                default:
                    return "answered";
            }
        }

        public static bool IsEmpty(AnswerDto a)
        {
            if (a == null) return true;
            if (a.OptionIds != null && a.OptionIds.Count > 0) return false;
            if (!string.IsNullOrWhiteSpace(a.Text)) return false;
            if (a.Number.HasValue) return false;
            if (!string.IsNullOrEmpty(a.Date)) return false;
            if (a.Matrix != null && a.Matrix.Values.Any(cols => cols != null && cols.Count > 0)) return false;
            return true;
        }

        public static bool EvaluateRule(RuleDto rule, QuestionDto question, AnswerDto answer)
        {
            if (IsEmpty(answer)) return false; // sin respuesta no se dispara ninguna regla
            var kind = RuleKind(question.Type);
            var ids = answer.OptionIds ?? new List<string>();
            decimal cmp;
            var hasCmp = decimal.TryParse(rule.Value ?? "", NumberStyles.Number, CultureInfo.InvariantCulture, out cmp);
            var num = answer.Number;

            switch (rule.Operator)
            {
                case "equals":
                    return kind == "number" ? (num.HasValue && hasCmp && num.Value == cmp) : ids.Contains(rule.Value);
                case "notEquals":
                    return kind == "number" ? !(num.HasValue && hasCmp && num.Value == cmp) : !ids.Contains(rule.Value);
                case "gt":
                    return num.HasValue && hasCmp && num.Value > cmp;
                case "lt":
                    return num.HasValue && hasCmp && num.Value < cmp;
                case "contains":
                    return (answer.Text ?? "").IndexOf(rule.Value ?? "", StringComparison.CurrentCultureIgnoreCase) >= 0;
                case "answered":
                    return true;
                default:
                    return false;
            }
        }

        public class VisibilityResult
        {
            public Dictionary<string, bool> Visible { get; set; }
            public string EndedAt { get; set; }

            public bool IsVisible(string questionId)
            {
                bool v;
                return !Visible.TryGetValue(questionId, out v) || v;
            }
        }

        /// <summary>
        /// Qué preguntas se muestran con las respuestas dadas. Misma semántica que
        /// SurveyLogic.computeVisibility: "show" oculta por defecto, "goto" salta,
        /// "end" termina, y las preguntas ocultas no disparan reglas.
        /// </summary>
        public static VisibilityResult ComputeVisibility(IList<QuestionDto> questions, IDictionary<string, AnswerDto> answers)
        {
            var ordered = questions.OrderBy(q => q.Order).ToList();
            var hiddenByDefault = new HashSet<string>(ordered
                .SelectMany(q => q.Rules ?? new List<RuleDto>())
                .Where(r => r.Action == "show" && !string.IsNullOrEmpty(r.TargetQuestionId))
                .Select(r => r.TargetQuestionId));

            var visible = ordered.ToDictionary(q => q.Id, q => !hiddenByDefault.Contains(q.Id));
            string skipUntil = null;
            string endedAt = null;

            foreach (var q in ordered)
            {
                if (endedAt != null) { visible[q.Id] = false; continue; }
                if (skipUntil != null)
                {
                    if (q.Id == skipUntil) skipUntil = null;
                    else { visible[q.Id] = false; continue; }
                }
                if (!visible[q.Id]) continue;

                AnswerDto answer;
                answers.TryGetValue(q.Id, out answer);
                foreach (var rule in q.Rules ?? new List<RuleDto>())
                {
                    if (!EvaluateRule(rule, q, answer)) continue;
                    if (rule.Action == "show" && rule.TargetQuestionId != null) visible[rule.TargetQuestionId] = true;
                    else if (rule.Action == "hide" && rule.TargetQuestionId != null) visible[rule.TargetQuestionId] = false;
                    else if (rule.Action == "goto") skipUntil = rule.TargetQuestionId;
                    else if (rule.Action == "end") endedAt = q.Id;
                }
            }
            return new VisibilityResult { Visible = visible, EndedAt = endedAt };
        }

        /// <summary>Error de una respuesta (mismos mensajes que el navegador) o null si es válida.</summary>
        public static string ValidateAnswer(QuestionDto q, AnswerDto a)
        {
            var v = q.Validation ?? new QuestionValidationDto();
            if (IsEmpty(a)) return q.Required ? "Esta pregunta es obligatoria." : null;

            if (a.OptionIds != null && a.OptionIds.Contains(OtherOption) && string.IsNullOrWhiteSpace(a.OtherText))
                return "Especifica tu respuesta en «" + (q.OtherLabel ?? "Otro") + "».";

            switch (q.Type)
            {
                case "singleChoice":
                case "dropdown":
                case "yesNo":
                    if (a.OptionIds.Count > 1) return "Selecciona solo una opción.";
                    break;
                case "multipleChoice":
                    var count = a.OptionIds.Count;
                    if (v.MinSelections.HasValue && count < v.MinSelections) return "Selecciona al menos " + v.MinSelections + " opciones.";
                    if (v.MaxSelections.HasValue && count > v.MaxSelections) return "Selecciona como máximo " + v.MaxSelections + " opciones.";
                    break;
                case "text":
                    var len = (a.Text ?? "").Trim().Length;
                    if (v.MinLength.HasValue && len < v.MinLength) return "Escribe al menos " + v.MinLength + " caracteres.";
                    if (v.MaxLength.HasValue && len > v.MaxLength) return "Máximo " + v.MaxLength + " caracteres.";
                    break;
                case "number":
                    if (!a.Number.HasValue) return "Ingresa un número válido.";
                    if (v.MinValue.HasValue && a.Number < v.MinValue) return "El valor mínimo es " + v.MinValue.Value.ToString(CultureInfo.InvariantCulture) + ".";
                    if (v.MaxValue.HasValue && a.Number > v.MaxValue) return "El valor máximo es " + v.MaxValue.Value.ToString(CultureInfo.InvariantCulture) + ".";
                    break;
                case "scale":
                    var min = v.ScaleMin ?? 1;
                    var max = v.ScaleMax ?? 5;
                    if (!a.Number.HasValue || a.Number < min || a.Number > max || a.Number != Math.Floor(a.Number.Value))
                        return "Selecciona un valor entre " + min + " y " + max + ".";
                    break;
                case "date":
                    DateTime date;
                    if (!DateTime.TryParseExact(a.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                        return "Ingresa una fecha válida.";
                    break;
                case "matrix":
                    var rows = q.Matrix != null ? q.Matrix.Rows : new List<OptionDto>();
                    var single = q.Matrix == null || q.Matrix.SelectionType != "multiple";
                    if (single && a.Matrix.Values.Any(cols => cols != null && cols.Count > 1)) return "Selecciona una sola opción por fila.";
                    if (q.Required)
                    {
                        var missing = rows.Count(r => !a.Matrix.ContainsKey(r.Id) || a.Matrix[r.Id] == null || a.Matrix[r.Id].Count == 0);
                        if (missing > 0) return "Responde todas las filas (faltan " + missing + ").";
                    }
                    break;
                case "dependent":
                    if (q.Required && q.Dependent != null && !q.Dependent.Multiple && a.OptionIds.Count < q.Dependent.Levels.Count)
                        return "Completa todos los niveles.";
                    break;
            }
            return null;
        }

        /// <summary>
        /// Valida un envío completo. Devuelve errores "P{n}: mensaje" y, en
        /// <paramref name="accepted"/>, solo las respuestas de preguntas visibles,
        /// ya depuradas (opciones, filas y columnas inexistentes se eliminan).
        /// </summary>
        public static List<string> ValidateSubmission(SurveyVersionDto version, IDictionary<string, AnswerDto> answers,
            out Dictionary<string, AnswerDto> accepted)
        {
            var errors = new List<string>();
            accepted = new Dictionary<string, AnswerDto>();
            var questions = version.Questions.OrderBy(q => q.Order).ToList();

            var clean = new Dictionary<string, AnswerDto>();
            foreach (var q in questions)
            {
                AnswerDto raw;
                if (answers != null && answers.TryGetValue(q.Id, out raw) && raw != null) clean[q.Id] = Sanitize(q, raw);
            }

            var visibility = ComputeVisibility(questions, clean);
            for (var i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                if (!visibility.IsVisible(q.Id)) continue; // oculta por lógica: ni se exige ni se guarda

                AnswerDto answer;
                clean.TryGetValue(q.Id, out answer);
                var error = ValidateAnswer(q, answer);
                if (error != null) errors.Add("P" + (i + 1) + ": " + error);
                else if (!IsEmpty(answer)) accepted[q.Id] = answer;
            }
            return errors;
        }

        /// <summary>Deja solo valores que existen en la definición de la pregunta.</summary>
        private static AnswerDto Sanitize(QuestionDto q, AnswerDto a)
        {
            var result = new AnswerDto();
            switch (q.Type)
            {
                case "text":
                    result.Text = a.Text;
                    break;
                case "scale":
                case "number":
                    result.Number = a.Number;
                    break;
                case "date":
                    result.Date = a.Date;
                    break;
                case "matrix":
                    var rowIds = q.Matrix == null ? new HashSet<string>() : new HashSet<string>(q.Matrix.Rows.Select(r => r.Id));
                    var colIds = q.Matrix == null ? new HashSet<string>() : new HashSet<string>(q.Matrix.Columns.Select(c => c.Id));
                    result.Matrix = new Dictionary<string, List<string>>();
                    foreach (var row in a.Matrix ?? new Dictionary<string, List<string>>())
                    {
                        if (!rowIds.Contains(row.Key) || row.Value == null) continue;
                        var cols = row.Value.Where(colIds.Contains).Distinct().ToList();
                        if (cols.Count > 0) result.Matrix[row.Key] = cols;
                    }
                    break;
                case "dependent":
                    var depIds = q.Dependent == null ? new HashSet<string>() : new HashSet<string>(q.Dependent.Options.Select(o => o.Id));
                    result.OptionIds = (a.OptionIds ?? new List<string>()).Where(depIds.Contains).Distinct().ToList();
                    break;
                default:
                    var optionIds = new HashSet<string>((q.Options ?? new List<OptionDto>()).Select(o => o.Id));
                    if (q.AllowOther) optionIds.Add(OtherOption);
                    result.OptionIds = (a.OptionIds ?? new List<string>()).Where(optionIds.Contains).Distinct().ToList();
                    if (result.OptionIds.Contains(OtherOption)) result.OtherText = (a.OtherText ?? "").Trim();
                    break;
            }
            return result;
        }
    }
}
