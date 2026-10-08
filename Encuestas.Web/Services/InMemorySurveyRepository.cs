using System;
using System.Collections.Generic;
using System.Linq;
using Encuestas.Web.Models.Dto;

namespace Encuestas.Web.Services
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

        private readonly IOrgCatalogProvider _catalogs;

        static InMemorySurveyRepository()
        {
            var seed = BuildSeedSurvey();
            Surveys[seed.Id] = seed;
        }

        public InMemorySurveyRepository(IOrgCatalogProvider catalogs)
        {
            _catalogs = catalogs;
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

        public OrgCatalogs GetCatalogs()
        {
            return _catalogs.GetCatalogs();
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
    /// Catálogos de ejemplo. Reemplazar por un proveedor que lea el directorio
    /// de RH / Active Directory (implementando IOrgCatalogProvider).
    /// </summary>
    public class DemoOrgCatalogProvider : IOrgCatalogProvider
    {
        private static readonly OrgCatalogs Catalogs = Build();

        public OrgCatalogs GetCatalogs()
        {
            return Catalogs;
        }

        private static OrgCatalogs Build()
        {
            var c = new OrgCatalogs();
            c.Roles.Add(new CatalogItem { Id = "ejecutivos", Name = "Ejecutivos" });
            c.Roles.Add(new CatalogItem { Id = "gerentes", Name = "Gerentes" });
            c.Segments.Add(new CatalogItem { Id = "bp", Name = "Banca Personal" });
            c.Segments.Add(new CatalogItem { Id = "premier", Name = "Premier" });
            c.Regions.Add(new CatalogItem { Id = "norte", Name = "Norte" });
            c.Regions.Add(new CatalogItem { Id = "centro", Name = "Centro" });
            c.Branches.Add(new BranchItem
            {
                Id = "b01", Name = "Sucursal Monterrey Centro", RegionId = "norte", SegmentId = "bp",
                Headcount = new Dictionary<string, int> { { "ejecutivos", 18 }, { "gerentes", 2 } }
            });
            c.Branches.Add(new BranchItem
            {
                Id = "b02", Name = "Sucursal Reforma", RegionId = "centro", SegmentId = "premier",
                Headcount = new Dictionary<string, int> { { "ejecutivos", 12 }, { "gerentes", 2 } }
            });
            return c;
        }
    }

    /// <summary>
    /// Composición mínima (el proyecto no usa contenedor de DI).
    /// Web.config -&gt; appSettings "Surveys:Repository": "Sql" o "InMemory".
    /// </summary>
    public static class SurveyServiceFactory
    {
        /// <param name="userName">Usuario de la petición (User.Identity.Name), para la bitácora.</param>
        public static SurveyService Create(string userName = null)
        {
            var settings = System.Configuration.ConfigurationManager.AppSettings;
            var catalogs = CreateCatalogProvider(settings);
            bool enforce;
            var options = new SurveyServiceOptions
            {
                EnforceAudience = bool.TryParse(settings["Surveys:EnforceAudience"], out enforce) && enforce,
                UserName = userName
            };

            if (string.Equals(settings["Surveys:Repository"], "Sql", StringComparison.OrdinalIgnoreCase))
            {
                var cs = System.Configuration.ConfigurationManager.ConnectionStrings["SurveysDb"];
                return new SurveyService(new SqlSurveyRepository(cs == null ? null : cs.ConnectionString, catalogs), options);
            }
            return new SurveyService(new InMemorySurveyRepository(catalogs), options);
        }

        /// <summary>Surveys:CatalogProvider = "Sql" (tablas Org*) o "Demo" (catálogos de ejemplo).</summary>
        private static IOrgCatalogProvider CreateCatalogProvider(System.Collections.Specialized.NameValueCollection settings)
        {
            if (!string.Equals(settings["Surveys:CatalogProvider"], "Sql", StringComparison.OrdinalIgnoreCase))
                return new DemoOrgCatalogProvider();

            int minutes;
            if (!int.TryParse(settings["Surveys:CatalogCacheMinutes"], out minutes) || minutes < 0) minutes = 10;
            var cs = System.Configuration.ConfigurationManager.ConnectionStrings["SurveysDb"];
            return new SqlOrgCatalogProvider(cs == null ? null : cs.ConnectionString, TimeSpan.FromMinutes(minutes));
        }
    }
}
