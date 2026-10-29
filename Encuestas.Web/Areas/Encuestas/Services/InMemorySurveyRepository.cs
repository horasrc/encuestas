using System;
using System.Collections.Generic;
using System.Linq;
using ModuloEncuestas.Models.Dto;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Repositorio en memoria (se pierde al reiniciar el AppPool). Solo para
    /// probar el modo servidor (Surveys:UseMockData = false) sin base de datos.
    /// En producción se usa SqlSurveyRepository (Surveys:Repository = Sql).
    /// </summary>
    public class InMemorySurveyRepository : ISurveyRepository
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, SurveyDto> Surveys = new Dictionary<string, SurveyDto>();
        private static readonly Dictionary<string, List<ResponseRecordDto>> Responses = new Dictionary<string, List<ResponseRecordDto>>();
        private static readonly HashSet<string> Respondents = new HashSet<string>();
        private static int _lastSurveyId;

        static InMemorySurveyRepository()
        {
            var seed = BuildSeedSurvey();
            Surveys[seed.Id] = seed;
        }

        public IList<SurveyDto> GetSurveys()
        {
            lock (Sync) return Surveys.Values.ToList();
        }

        public SurveyDto GetSurvey(string surveyId)
        {
            lock (Sync)
            {
                SurveyDto survey;
                return Surveys.TryGetValue(surveyId ?? "", out survey) ? survey : null;
            }
        }

        public void SaveSurvey(SurveyDto survey)
        {
            lock (Sync)
            {
                if (string.IsNullOrEmpty(survey.Id))
                {
                    // Como una columna IDENTITY: nunca reutiliza IDs (ni de encuestas eliminadas),
                    // para que su bitácora no se mezcle con la de una encuesta nueva.
                    foreach (var key in Surveys.Keys)
                    {
                        int n;
                        if (int.TryParse(key, out n) && n > _lastSurveyId) _lastSurveyId = n;
                    }
                    survey.Id = (++_lastSurveyId).ToString();
                }
                Surveys[survey.Id] = survey;
            }
        }

        public void DeleteSurvey(string surveyId)
        {
            lock (Sync) Surveys.Remove(surveyId ?? "");
        }

        private static readonly List<AuditEntryDto> Audit = new List<AuditEntryDto>();

        public void AddAudit(AuditEntryDto entry)
        {
            lock (Sync) Audit.Add(entry);
        }

        public IList<AuditEntryDto> GetAudit(string surveyId, int max)
        {
            lock (Sync) return Audit.Where(a => a.SurveyId == surveyId).Reverse().Take(max).ToList();
        }

        public IList<ResponseRecordDto> GetResponses(string versionId)
        {
            lock (Sync)
            {
                List<ResponseRecordDto> list;
                return Responses.TryGetValue(versionId, out list) ? list.ToList() : new List<ResponseRecordDto>();
            }
        }

        public void AddResponse(string versionId, ResponseRecordDto response, string userHash)
        {
            lock (Sync)
            {
                if (!Responses.ContainsKey(versionId)) Responses[versionId] = new List<ResponseRecordDto>();
                response.UserHash = userHash;
                Responses[versionId].Add(response);
                if (userHash != null) Respondents.Add(versionId + "|" + userHash);

                var version = Surveys.Values.SelectMany(s => s.Versions).FirstOrDefault(v => v.Id == versionId);
                if (version != null) version.ResponseCount++;
            }
        }

        public bool HasUserResponded(string versionId, string userHash)
        {
            lock (Sync) return Respondents.Contains(versionId + "|" + userHash);
        }

        public ResponseRecordDto FindLatestResponse(string surveyId, IList<string> questionIds, string value, out string versionId)
        {
            versionId = null;
            lock (Sync)
            {
                SurveyDto survey;
                if (!Surveys.TryGetValue(surveyId ?? "", out survey)) return null;
                ResponseRecordDto best = null;
                foreach (var v in survey.Versions)
                {
                    List<ResponseRecordDto> list;
                    if (!Responses.TryGetValue(v.Id, out list)) continue;
                    foreach (var r in list)
                    {
                        var match = questionIds.Any(k =>
                        {
                            AnswerDto a;
                            return r.Answers != null && r.Answers.TryGetValue(k, out a) && a != null &&
                                string.Equals((a.Text ?? "").Trim(), value, StringComparison.OrdinalIgnoreCase);
                        });
                        // SubmittedAt es ISO ("s"): se compara como texto; a igual hora gana la última guardada.
                        if (match && (best == null || string.CompareOrdinal(r.SubmittedAt, best.SubmittedAt) >= 0))
                        {
                            best = r;
                            versionId = v.Id;
                        }
                    }
                }
                return best;
            }
        }

        // -----------------------------------------------------------------
        // Datos semilla (mínimos). La demo completa vive en Scripts/survey/mock-data.js.
        // -----------------------------------------------------------------

        private static SurveyDto BuildSeedSurvey()
        {
            var q1 = new QuestionDto
            {
                Id = "q1", Order = 1, Type = "singleChoice", Required = true,
                Text = "¿Qué tan útil fue el material de la campaña?",
                Options = new List<OptionDto>
                {
                    new OptionDto { Id = "q1o1", Text = "Muy útil" },
                    new OptionDto { Id = "q1o2", Text = "Útil" },
                    new OptionDto { Id = "q1o3", Text = "Poco útil" }
                }
            };
            var q2 = new QuestionDto
            {
                Id = "q2", Order = 2, Type = "text",
                Text = "¿Qué mejorarías en esta campaña?",
                Validation = new QuestionValidationDto { TextMode = "long", MaxLength = 500 }
            };

            var version = new SurveyVersionDto
            {
                Id = "v1", Number = 1, Status = SurveyStatuses.Draft, CreatedAt = DateTime.Now,
                Settings = new PublishSettingsDto
                {
                    StartDate = DateTime.Today.ToString("yyyy-MM-dd"),
                    EndDate = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),
                    RequireAuth = true, Anonymous = true,
                    WelcomeMessage = "Tu opinión nos ayuda a mejorar.",
                    ThankYouMessage = "¡Gracias por tu respuesta!"
                }
            };
            version.Questions.Add(q1);
            version.Questions.Add(q2);

            var survey = new SurveyDto { Id = "1", Name = "Experiencia con campañas comerciales" };
            survey.Versions.Add(version);
            return survey;
        }
    }

    /// <summary>
    /// Composición mínima (el proyecto no usa contenedor de DI).
    /// Web.config -&gt; appSettings "Surveys:Repository": "Sql" o "InMemory".
    /// </summary>
    public static class SurveyServiceFactory
    {
        /// <param name="options">Usuario, segmento y tramo de control de la petición (ver Infrastructure.SurveyAccess).</param>
        public static SurveyService Create(SurveyServiceOptions options = null)
        {
            options = options ?? new SurveyServiceOptions();
            var settings = System.Configuration.ConfigurationManager.AppSettings;
            if (string.Equals(settings["Surveys:Repository"], "Sql", StringComparison.OrdinalIgnoreCase))
            {
                var cs = System.Configuration.ConfigurationManager.ConnectionStrings[ConnectionStringName(settings)];
                return new SurveyService(new SqlSurveyRepository(cs == null ? null : cs.ConnectionString, options.UserName), options);
            }
            return new SurveyService(new InMemorySurveyRepository(), options);
        }

        /// <summary>
        /// Nombre de la cadena de conexión: Surveys:ConnectionStringName (por defecto "SurveysDb").
        /// Permite reutilizar la cadena que el portal ya tiene, sin duplicarla.
        /// </summary>
        private static string ConnectionStringName(System.Collections.Specialized.NameValueCollection settings)
        {
            var name = settings["Surveys:ConnectionStringName"];
            return string.IsNullOrWhiteSpace(name) ? "SurveysDb" : name;
        }
    }
}
