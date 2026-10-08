using System;
using System.Collections.Generic;
using System.Linq;
using ModuloEncuestas.Models.Dto;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Copia profunda de una versión generando IDs nuevos para preguntas, opciones,
    /// filas/columnas, opciones dependientes y reglas. Las referencias internas
    /// (regla -> pregunta destino, regla -> opción, opción dependiente -> padre)
    /// se reescriben con los IDs nuevos para que la copia sea independiente.
    /// Equivalente en JS: SurveyUtils.cloneVersion().
    /// </summary>
    public static class SurveyVersionCloner
    {
        public static SurveyVersionDto Clone(SurveyVersionDto source, int newNumber)
        {
            var map = new Dictionary<string, string>();
            Func<string, string> newId = delegate(string old)
            {
                if (string.IsNullOrEmpty(old)) return old;
                string mapped;
                if (!map.TryGetValue(old, out mapped))
                {
                    mapped = NewId("x");
                    map[old] = mapped;
                }
                return mapped;
            };
            Func<string, string> remap = delegate(string old)
            {
                string mapped;
                return old != null && map.TryGetValue(old, out mapped) ? mapped : old;
            };

            var copy = new SurveyVersionDto
            {
                Id = NewId("v"),
                Number = newNumber,
                Status = SurveyStatuses.Draft,
                ResponseCount = 0,
                CreatedAt = DateTime.Now,
                PublishedAt = null,
                Settings = CloneSettings(source.Settings),
                Segmentation = CloneSegmentation(source.Segmentation)
            };

            // 1) Primera pasada: copiar estructura y registrar IDs nuevos.
            foreach (var q in source.Questions.OrderBy(x => x.Order))
            {
                var nq = new QuestionDto
                {
                    Id = newId(q.Id),
                    Order = q.Order,
                    Type = q.Type,
                    Text = q.Text,
                    HelpText = q.HelpText,
                    Required = q.Required,
                    RandomizeOptions = q.RandomizeOptions,
                    AllowOther = q.AllowOther,
                    OtherLabel = q.OtherLabel,
                    Validation = CloneValidation(q.Validation),
                    Options = q.Options.Select(o => new OptionDto { Id = newId(o.Id), Text = o.Text }).ToList()
                };

                if (q.Matrix != null)
                {
                    nq.Matrix = new MatrixDto
                    {
                        SelectionType = q.Matrix.SelectionType,
                        Rows = q.Matrix.Rows.Select(o => new OptionDto { Id = newId(o.Id), Text = o.Text }).ToList(),
                        Columns = q.Matrix.Columns.Select(o => new OptionDto { Id = newId(o.Id), Text = o.Text }).ToList()
                    };
                }

                if (q.Dependent != null)
                {
                    nq.Dependent = new DependentDto
                    {
                        Multiple = q.Dependent.Multiple,
                        Levels = q.Dependent.Levels.Select(l => new DependentLevelDto { Id = newId(l.Id), Name = l.Name }).ToList(),
                        Options = q.Dependent.Options.Select(o => new DependentOptionDto
                        {
                            Id = newId(o.Id), ParentId = o.ParentId, Level = o.Level, Text = o.Text
                        }).ToList()
                    };
                }

                nq.Rules = q.Rules.Select(r => new RuleDto
                {
                    Id = newId(r.Id), Operator = r.Operator, Value = r.Value, Action = r.Action, TargetQuestionId = r.TargetQuestionId
                }).ToList();

                copy.Questions.Add(nq);
            }

            // 2) Segunda pasada: reescribir referencias cruzadas con los IDs nuevos.
            foreach (var q in copy.Questions)
            {
                if (q.Dependent != null)
                {
                    foreach (var o in q.Dependent.Options) o.ParentId = remap(o.ParentId);
                }
                foreach (var r in q.Rules)
                {
                    r.Value = remap(r.Value);
                    r.TargetQuestionId = remap(r.TargetQuestionId);
                }
            }

            return copy;
        }

        public static string NewId(string prefix)
        {
            return prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 10);
        }

        private static PublishSettingsDto CloneSettings(PublishSettingsDto s)
        {
            if (s == null) return new PublishSettingsDto();
            return new PublishSettingsDto
            {
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                AllowMultipleResponses = s.AllowMultipleResponses,
                Anonymous = s.Anonymous,
                RequireAuth = s.RequireAuth,
                WelcomeMessage = s.WelcomeMessage,
                ThankYouMessage = s.ThankYouMessage
            };
        }

        private static SegmentationDto CloneSegmentation(SegmentationDto s)
        {
            if (s == null) return new SegmentationDto();
            return new SegmentationDto
            {
                Mode = s.Mode,
                Roles = new List<string>(s.Roles ?? new List<string>()),
                Segments = new List<string>(s.Segments ?? new List<string>()),
                Regions = new List<string>(s.Regions ?? new List<string>()),
                Branches = new List<string>(s.Branches ?? new List<string>()),
                Users = new List<string>(s.Users ?? new List<string>())
            };
        }

        private static QuestionValidationDto CloneValidation(QuestionValidationDto v)
        {
            return v == null ? new QuestionValidationDto() : v.Copy();
        }
    }

    /// <summary>Valores de texto de SurveyVersionDto.Status.</summary>
    public static class SurveyStatuses
    {
        public const string Draft = "Draft";
        public const string Published = "Published";
        public const string Closed = "Closed";
        public const string Archived = "Archived";
    }
}
