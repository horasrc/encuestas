using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Web.Script.Serialization;
using ModuloEncuestas.Models.Dto;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Repositorio SQL Server (ADO.NET) sobre las tablas de Database/schema.sql.
    ///
    /// Traducción de identificadores DTO &lt;-&gt; BD:
    ///   Survey.Id / SurveyVersion.Id      -> SurveyId / SurveyVersionId (entero como texto: "12")
    ///   Pregunta, opción, fila, columna,
    ///   opción dependiente                -> columna ClientKey (ID que generó el navegador)
    ///   Regla / nivel dependiente         -> "r{ConditionalRuleId}" / "l{DependentLevelId}"
    /// Mantener los ClientKey estables permite que las reglas condicionales y las
    /// respuestas sigan apuntando a lo mismo aunque el contenido se reescriba.
    ///
    /// Guardado: el contenido de una versión EDITABLE (borrador, o publicada sin
    /// respuestas) se reemplaza completo dentro de una transacción. Las versiones
    /// con respuestas solo actualizan su estado; nunca se tocan sus preguntas
    /// (además lo impide el trigger TR_Question_Immutable).
    /// </summary>
    public class SqlSurveyRepository : ISurveyRepository
    {
        private const string OtherKey = "__other__";

        private const byte RoleChoice = 1;
        private const byte RoleMatrixRow = 2;
        private const byte RoleMatrixColumn = 3;

        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer();
        private static readonly object TypeLock = new object();
        private static Dictionary<string, int> _questionTypeIds;

        private static readonly Dictionary<string, string> AudienceDimensions = new Dictionary<string, string>
        {
            { "Role", "roles" }, { "Segment", "segments" }, { "Region", "regions" }, { "Branch", "branches" }, { "User", "users" }
        };

        private readonly string _connectionString;
        private readonly IOrgCatalogProvider _catalogs;
        private readonly string _userName;

        /// <param name="userName">Usuario de la petición (SurveyUser), para CreatedBy/UpdatedBy.</param>
        public SqlSurveyRepository(string connectionString, IOrgCatalogProvider catalogs, string userName = null)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Falta la cadena de conexión 'SurveysDb'.");
            _connectionString = connectionString;
            _catalogs = catalogs;
            _userName = userName;
        }

        public OrgCatalogs GetCatalogs()
        {
            return _catalogs.GetCatalogs();
        }

        // =================================================================
        // LECTURA
        // =================================================================

        /// <summary>Encuestas con sus versiones (sin preguntas), para listados.</summary>
        public IList<SurveyDto> GetSurveys()
        {
            const string sql = @"
SELECT SurveyId, Name, Description FROM enc.Survey WHERE IsDeleted = 0 ORDER BY CreatedAt DESC;
SELECT v.SurveyId, v.SurveyVersionId, v.VersionNumber, v.Status, v.StartDate, v.EndDate, v.AllowMultipleResponses,
       v.IsAnonymous, v.RequiresAuthentication, v.WelcomeMessage, v.ThankYouMessage, v.ResponseCount, v.CreatedAt, v.PublishedAt, v.AudienceMode
FROM enc.SurveyVersion v JOIN enc.Survey s ON s.SurveyId = v.SurveyId
WHERE s.IsDeleted = 0 ORDER BY v.SurveyId, v.VersionNumber;";

            var surveys = new Dictionary<int, SurveyDto>();
            var ordered = new List<SurveyDto>();
            using (var conn = Open())
            using (var cmd = Command(conn, null, sql))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var s = new SurveyDto { Id = Key(r.GetInt32(0)), Name = r.GetString(1), Description = Str(r, 2) };
                    surveys[r.GetInt32(0)] = s;
                    ordered.Add(s);
                }
                r.NextResult();
                while (r.Read())
                {
                    SurveyDto s;
                    if (surveys.TryGetValue(r.GetInt32(0), out s)) s.Versions.Add(ReadVersionHeader(r, 1));
                }
            }
            return ordered;
        }

        /// <summary>Encuesta completa: versiones, preguntas, opciones, dependientes, reglas y segmentación.</summary>
        public SurveyDto GetSurvey(string surveyId)
        {
            int id;
            if (!TryParseKey(surveyId, out id)) return null;

            const string versionFilter = "JOIN enc.SurveyVersion v ON v.SurveyVersionId = q.SurveyVersionId WHERE v.SurveyId = @SurveyId";
            var sql = @"
SELECT SurveyId, Name, Description FROM enc.Survey WHERE SurveyId = @SurveyId AND IsDeleted = 0;

SELECT v.SurveyId, v.SurveyVersionId, v.VersionNumber, v.Status, v.StartDate, v.EndDate, v.AllowMultipleResponses,
       v.IsAnonymous, v.RequiresAuthentication, v.WelcomeMessage, v.ThankYouMessage, v.ResponseCount, v.CreatedAt, v.PublishedAt, v.AudienceMode
FROM enc.SurveyVersion v WHERE v.SurveyId = @SurveyId ORDER BY v.VersionNumber;

SELECT q.QuestionId, q.SurveyVersionId, q.ClientKey, t.Code, q.SortOrder, q.Text, q.HelpText,
       q.IsRequired, q.RandomizeOptions, q.AllowOther, q.OtherLabel, q.SettingsJson
FROM enc.Question q JOIN enc.QuestionType t ON t.QuestionTypeId = q.QuestionTypeId
" + versionFilter + @" ORDER BY q.SurveyVersionId, q.SortOrder;

SELECT o.QuestionId, o.Role, o.ClientKey, o.Text
FROM enc.QuestionOption o JOIN enc.Question q ON q.QuestionId = o.QuestionId
" + versionFilter + @" ORDER BY o.QuestionId, o.Role, o.SortOrder;

SELECT l.QuestionId, l.DependentLevelId, l.Name
FROM enc.DependentLevel l JOIN enc.Question q ON q.QuestionId = l.QuestionId
" + versionFilter + @" ORDER BY l.QuestionId, l.LevelIndex;

SELECT d.QuestionId, d.ClientKey, p.ClientKey AS ParentKey, d.LevelIndex, d.Text
FROM enc.DependentOption d
JOIN enc.Question q ON q.QuestionId = d.QuestionId
LEFT JOIN enc.DependentOption p ON p.DependentOptionId = d.ParentOptionId
" + versionFilter + @" ORDER BY d.QuestionId, d.SortOrder;

SELECT r.QuestionId, r.ConditionalRuleId, r.Operator, r.CompareValue, r.Action, tq.ClientKey AS TargetKey
FROM enc.ConditionalRule r
JOIN enc.Question q ON q.QuestionId = r.QuestionId
LEFT JOIN enc.Question tq ON tq.QuestionId = r.TargetQuestionId
" + versionFilter + @" ORDER BY r.QuestionId, r.SortOrder;

SELECT a.SurveyVersionId, a.Dimension, a.Value
FROM enc.SurveyAudienceRule a JOIN enc.SurveyVersion v ON v.SurveyVersionId = a.SurveyVersionId
WHERE v.SurveyId = @SurveyId ORDER BY a.AudienceRuleId;";

            var versions = new Dictionary<int, SurveyVersionDto>();
            var questions = new Dictionary<int, QuestionDto>();
            var optionParents = new Dictionary<QuestionDto, Dictionary<string, string>>();
            SurveyDto survey;

            using (var conn = Open())
            using (var cmd = Command(conn, null, sql, "@SurveyId", id))
            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) return null;
                survey = new SurveyDto { Id = Key(r.GetInt32(0)), Name = r.GetString(1), Description = Str(r, 2) };

                // Versiones
                r.NextResult();
                while (r.Read())
                {
                    var v = ReadVersionHeader(r, 1);
                    versions[r.GetInt32(1)] = v;
                    survey.Versions.Add(v);
                }

                // Preguntas
                r.NextResult();
                while (r.Read())
                {
                    var q = new QuestionDto
                    {
                        Id = r.GetString(2),
                        Type = r.GetString(3),
                        Order = r.GetInt32(4),
                        Text = r.GetString(5),
                        HelpText = Str(r, 6),
                        Required = r.GetBoolean(7),
                        RandomizeOptions = r.GetBoolean(8),
                        AllowOther = r.GetBoolean(9),
                        OtherLabel = Str(r, 10)
                    };
                    var parents = ApplySettings(q, Str(r, 11));
                    if (parents != null) optionParents[q] = parents;
                    questions[r.GetInt32(0)] = q;
                    versions[r.GetInt32(1)].Questions.Add(q);
                }

                // Opciones, filas y columnas
                r.NextResult();
                while (r.Read())
                {
                    var q = questions[r.GetInt32(0)];
                    var option = new OptionDto { Id = r.GetString(2), Text = r.GetString(3) };
                    var role = r.GetByte(1);
                    if (role == RoleChoice) q.Options.Add(option);
                    else if (role == RoleMatrixRow) EnsureMatrix(q).Rows.Add(option);
                    else EnsureMatrix(q).Columns.Add(option);

                    Dictionary<string, string> parents;
                    string parentId;
                    if (role == RoleChoice && optionParents.TryGetValue(q, out parents) && parents.TryGetValue(option.Id, out parentId)) option.ParentId = parentId;
                }

                // Niveles dependientes
                r.NextResult();
                while (r.Read())
                {
                    EnsureDependent(questions[r.GetInt32(0)]).Levels.Add(new DependentLevelDto { Id = "l" + Key(r.GetInt32(1)), Name = r.GetString(2) });
                }

                // Opciones dependientes
                r.NextResult();
                while (r.Read())
                {
                    EnsureDependent(questions[r.GetInt32(0)]).Options.Add(new DependentOptionDto
                    {
                        Id = r.GetString(1), ParentId = Str(r, 2), Level = r.GetInt32(3), Text = r.GetString(4)
                    });
                }

                // Reglas condicionales
                r.NextResult();
                while (r.Read())
                {
                    questions[r.GetInt32(0)].Rules.Add(new RuleDto
                    {
                        Id = "r" + Key(r.GetInt32(1)),
                        Operator = r.GetString(2),
                        Value = Str(r, 3) ?? "",
                        Action = r.GetString(4),
                        TargetQuestionId = Str(r, 5)
                    });
                }

                // Segmentación
                r.NextResult();
                while (r.Read())
                {
                    SurveyVersionDto v;
                    string list;
                    if (!versions.TryGetValue(r.GetInt32(0), out v) || !AudienceDimensions.TryGetValue(r.GetString(1), out list)) continue;
                    AudienceList(v.Segmentation, list).Add(r.GetString(2));
                }
            }
            return survey;
        }

        // =================================================================
        // ESCRITURA
        // =================================================================

        /// <summary>
        /// Inserta o actualiza el agregado completo. Los IDs nuevos (encuesta y
        /// versiones creadas en esta llamada) se escriben de vuelta en el DTO.
        /// </summary>
        public void SaveSurvey(SurveyDto survey)
        {
            using (var conn = Open())
            using (var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    var surveyId = UpsertSurvey(conn, tx, survey);
                    survey.Id = Key(surveyId);

                    // Estado real en BD (bloqueado hasta el commit para evitar carreras con respuestas entrantes).
                    var stored = LoadVersionStates(conn, tx, surveyId);

                    // Orden: primero las que salen de Publicada/Borrador, para no violar los índices únicos
                    // (un solo borrador y una sola publicada por encuesta).
                    foreach (var v in survey.Versions.OrderBy(x => StatusWriteOrder(x.Status)).ToList())
                    {
                        int versionId;
                        VersionState state;
                        if (TryParseKey(v.Id, out versionId) && stored.TryGetValue(versionId, out state))
                        {
                            var editable = state.Status == 1 || (state.Status == 2 && state.ResponseCount == 0);
                            if (editable)
                            {
                                UpdateVersion(conn, tx, versionId, v);
                                WriteVersionContent(conn, tx, versionId, v);
                            }
                            else
                            {
                                UpdateVersionStatus(conn, tx, versionId, v);
                            }
                            v.ResponseCount = state.ResponseCount; // el contador lo mantiene AddResponse
                        }
                        else
                        {
                            versionId = InsertVersion(conn, tx, surveyId, v);
                            v.Id = Key(versionId);
                            v.ResponseCount = 0;
                            WriteVersionContent(conn, tx, versionId, v);
                        }
                    }
                    // Las versiones que existan en BD pero no en el DTO no se borran: son histórico.

                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        public void AddAudit(AuditEntryDto entry)
        {
            int surveyId;
            if (!TryParseKey(entry.SurveyId, out surveyId)) return;
            var detail = entry.Detail != null && entry.Detail.Length > 500 ? entry.Detail.Substring(0, 500) : entry.Detail;
            var user = string.IsNullOrEmpty(entry.UserName) ? "sistema" : entry.UserName;
            using (var conn = Open())
            using (var cmd = Command(conn, null, @"
INSERT INTO enc.SurveyAuditLog (SurveyId, VersionNumber, Action, Detail, UserName)
VALUES (@S, @V, @A, @D, @U);",
                "@S", surveyId, "@V", entry.VersionNumber, "@A", entry.Action, "@D", detail, "@U", user.Length > 100 ? user.Substring(0, 100) : user))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public IList<AuditEntryDto> GetAudit(string surveyId, int max)
        {
            var result = new List<AuditEntryDto>();
            int id;
            if (!TryParseKey(surveyId, out id)) return result;
            using (var conn = Open())
            using (var cmd = Command(conn, null, @"
SELECT TOP (@Max) VersionNumber, Action, Detail, UserName, CreatedAt
FROM enc.SurveyAuditLog WHERE SurveyId = @S ORDER BY CreatedAt DESC, AuditId DESC;", "@Max", max, "@S", id))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new AuditEntryDto
                    {
                        SurveyId = surveyId,
                        VersionNumber = r.IsDBNull(0) ? (int?)null : r.GetInt32(0),
                        Action = r.GetString(1),
                        Detail = Str(r, 2),
                        UserName = r.GetString(3),
                        CreatedAt = r.GetDateTime(4)
                    });
                }
            }
            return result;
        }

        public void DeleteSurvey(string surveyId)
        {
            int id;
            if (!TryParseKey(surveyId, out id)) return;
            using (var conn = Open())
            using (var cmd = Command(conn, null, "UPDATE enc.Survey SET IsDeleted = 1 WHERE SurveyId = @Id", "@Id", id))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private int UpsertSurvey(SqlConnection conn, SqlTransaction tx, SurveyDto survey)
        {
            int id;
            if (TryParseKey(survey.Id, out id))
            {
                var updated = Command(conn, tx,
                    "UPDATE enc.Survey SET Name = @Name, Description = @Description WHERE SurveyId = @Id AND IsDeleted = 0",
                    "@Name", survey.Name, "@Description", survey.Description, "@Id", id).ExecuteNonQuery();
                if (updated > 0) return id;
            }
            return Convert.ToInt32(Command(conn, tx,
                "INSERT INTO enc.Survey (Name, Description, CreatedBy) VALUES (@Name, @Description, @User); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                "@Name", survey.Name ?? "Encuesta sin nombre", "@Description", survey.Description, "@User", CurrentUser()).ExecuteScalar());
        }

        private struct VersionState
        {
            public byte Status;
            public int ResponseCount;
        }

        private static Dictionary<int, VersionState> LoadVersionStates(SqlConnection conn, SqlTransaction tx, int surveyId)
        {
            var result = new Dictionary<int, VersionState>();
            using (var r = Command(conn, tx,
                "SELECT SurveyVersionId, Status, ResponseCount FROM enc.SurveyVersion WITH (UPDLOCK, HOLDLOCK) WHERE SurveyId = @SurveyId",
                "@SurveyId", surveyId).ExecuteReader())
            {
                while (r.Read()) result[r.GetInt32(0)] = new VersionState { Status = r.GetByte(1), ResponseCount = r.GetInt32(2) };
            }
            return result;
        }

        private int InsertVersion(SqlConnection conn, SqlTransaction tx, int surveyId, SurveyVersionDto v)
        {
            var s = v.Settings ?? new PublishSettingsDto();
            var status = StatusToByte(v.Status);
            return Convert.ToInt32(Command(conn, tx, @"
INSERT INTO enc.SurveyVersion (SurveyId, VersionNumber, Status, StartDate, EndDate, AllowMultipleResponses, IsAnonymous,
    RequiresAuthentication, WelcomeMessage, ThankYouMessage, AudienceMode, CreatedBy, PublishedAt)
VALUES (@SurveyId, @Number, @Status, @StartDate, @EndDate, @Multiple, @Anonymous, @Auth, @Welcome, @ThankYou, @Mode, @User, @PublishedAt);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                "@SurveyId", surveyId, "@Number", v.Number, "@Status", status,
                "@StartDate", ParseDate(s.StartDate), "@EndDate", ParseDate(s.EndDate),
                "@Multiple", s.AllowMultipleResponses, "@Anonymous", s.Anonymous, "@Auth", s.RequireAuth,
                "@Welcome", s.WelcomeMessage, "@ThankYou", s.ThankYouMessage, "@Mode", AudienceMode(v), "@User", CurrentUser(),
                "@PublishedAt", status == 2 ? (object)(v.PublishedAt ?? DateTime.Now) : null).ExecuteScalar());
        }

        private static void UpdateVersion(SqlConnection conn, SqlTransaction tx, int versionId, SurveyVersionDto v)
        {
            var s = v.Settings ?? new PublishSettingsDto();
            Command(conn, tx, @"
UPDATE enc.SurveyVersion SET
    Status = @Status, StartDate = @StartDate, EndDate = @EndDate, AllowMultipleResponses = @Multiple,
    IsAnonymous = @Anonymous, RequiresAuthentication = @Auth, WelcomeMessage = @Welcome, ThankYouMessage = @ThankYou,
    AudienceMode = @Mode,
    PublishedAt = CASE WHEN @Status = 2 THEN COALESCE(PublishedAt, @PublishedAt, SYSDATETIME()) ELSE PublishedAt END,
    ClosedAt = CASE WHEN @Status = 3 AND ClosedAt IS NULL THEN SYSDATETIME() ELSE ClosedAt END
WHERE SurveyVersionId = @Id;",
                "@Status", StatusToByte(v.Status), "@StartDate", ParseDate(s.StartDate), "@EndDate", ParseDate(s.EndDate),
                "@Multiple", s.AllowMultipleResponses, "@Anonymous", s.Anonymous, "@Auth", s.RequireAuth,
                "@Welcome", s.WelcomeMessage, "@ThankYou", s.ThankYouMessage, "@Mode", AudienceMode(v), "@PublishedAt", v.PublishedAt,
                "@Id", versionId).ExecuteNonQuery();
        }

        /// <summary>Versiones con respuestas: solo cambia el estado (cerrar / archivar).</summary>
        private static void UpdateVersionStatus(SqlConnection conn, SqlTransaction tx, int versionId, SurveyVersionDto v)
        {
            Command(conn, tx, @"
UPDATE enc.SurveyVersion SET
    Status = @Status,
    ClosedAt = CASE WHEN @Status = 3 AND ClosedAt IS NULL THEN SYSDATETIME() ELSE ClosedAt END
WHERE SurveyVersionId = @Id;",
                "@Status", StatusToByte(v.Status), "@Id", versionId).ExecuteNonQuery();
        }

        /// <summary>Reemplaza preguntas, opciones, dependientes, reglas y segmentación de una versión editable.</summary>
        private static void WriteVersionContent(SqlConnection conn, SqlTransaction tx, int versionId, SurveyVersionDto v)
        {
            // 1) Borrado explícito en orden de dependencias (no se confía solo en cascadas).
            Command(conn, tx, @"
DELETE r FROM enc.ConditionalRule r JOIN enc.Question q ON q.QuestionId = r.QuestionId WHERE q.SurveyVersionId = @V;
DELETE r FROM enc.ConditionalRule r JOIN enc.Question q ON q.QuestionId = r.TargetQuestionId WHERE q.SurveyVersionId = @V;
UPDATE d SET ParentOptionId = NULL FROM enc.DependentOption d JOIN enc.Question q ON q.QuestionId = d.QuestionId WHERE q.SurveyVersionId = @V;
DELETE d FROM enc.DependentOption d JOIN enc.Question q ON q.QuestionId = d.QuestionId WHERE q.SurveyVersionId = @V;
DELETE l FROM enc.DependentLevel l JOIN enc.Question q ON q.QuestionId = l.QuestionId WHERE q.SurveyVersionId = @V;
DELETE o FROM enc.QuestionOption o JOIN enc.Question q ON q.QuestionId = o.QuestionId WHERE q.SurveyVersionId = @V;
DELETE FROM enc.Question WHERE SurveyVersionId = @V;
DELETE FROM enc.SurveyAudienceRule WHERE SurveyVersionId = @V;", "@V", versionId).ExecuteNonQuery();

            var typeIds = QuestionTypeIds(conn, tx);
            var questionIds = new Dictionary<string, int>();
            var ordered = (v.Questions ?? new List<QuestionDto>()).OrderBy(q => q.Order).ToList();

            // 2) Preguntas y sus opciones.
            for (var i = 0; i < ordered.Count; i++)
            {
                var q = ordered[i];
                int typeId;
                if (!typeIds.TryGetValue(q.Type ?? "", out typeId)) throw new InvalidOperationException("Tipo de pregunta desconocido: " + q.Type);

                var questionId = Convert.ToInt32(Command(conn, tx, @"
INSERT INTO enc.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, HelpText, IsRequired,
    RandomizeOptions, AllowOther, OtherLabel, SettingsJson)
VALUES (@V, @Type, @Key, @Order, @Text, @Help, @Required, @Random, @Other, @OtherLabel, @Settings);
SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    "@V", versionId, "@Type", typeId, "@Key", q.Id, "@Order", i + 1, "@Text", q.Text ?? "",
                    "@Help", q.HelpText, "@Required", q.Required, "@Random", q.RandomizeOptions, "@Other", q.AllowOther,
                    "@OtherLabel", q.OtherLabel, "@Settings", SerializeSettings(q)).ExecuteScalar());
                questionIds[q.Id] = questionId;
                q.Order = i + 1;

                InsertOptions(conn, tx, questionId, RoleChoice, q.Options);
                if (q.Matrix != null)
                {
                    InsertOptions(conn, tx, questionId, RoleMatrixRow, q.Matrix.Rows);
                    InsertOptions(conn, tx, questionId, RoleMatrixColumn, q.Matrix.Columns);
                }
                if (q.Dependent != null) InsertDependent(conn, tx, questionId, q.Dependent);
            }

            // 3) Reglas (segunda pasada: los destinos ya tienen QuestionId).
            foreach (var q in ordered)
            {
                var sort = 0;
                foreach (var rule in q.Rules ?? new List<RuleDto>())
                {
                    int targetId;
                    object target = null;
                    if (rule.Action != "end")
                    {
                        if (string.IsNullOrEmpty(rule.TargetQuestionId) || !questionIds.TryGetValue(rule.TargetQuestionId, out targetId)) continue; // destino inexistente: se descarta
                        target = targetId;
                    }
                    Command(conn, tx, @"
INSERT INTO enc.ConditionalRule (QuestionId, Operator, CompareValue, Action, TargetQuestionId, SortOrder)
VALUES (@Q, @Op, @Value, @Action, @Target, @Sort);",
                        "@Q", questionIds[q.Id], "@Op", rule.Operator, "@Value", rule.Value, "@Action", rule.Action,
                        "@Target", target, "@Sort", ++sort).ExecuteNonQuery();
                }
            }

            // 4) Segmentación (sin filas = todos los colaboradores).
            var seg = v.Segmentation ?? new SegmentationDto();
            if (seg.Mode == "segmented")
            {
                foreach (var dim in AudienceDimensions)
                {
                    foreach (var value in AudienceList(seg, dim.Value).Distinct())
                    {
                        Command(conn, tx, "INSERT INTO enc.SurveyAudienceRule (SurveyVersionId, Dimension, Value) VALUES (@V, @D, @Value);",
                            "@V", versionId, "@D", dim.Key, "@Value", value).ExecuteNonQuery();
                    }
                }
            }
        }

        private static void InsertOptions(SqlConnection conn, SqlTransaction tx, int questionId, byte role, IList<OptionDto> options)
        {
            if (options == null) return;
            for (var i = 0; i < options.Count; i++)
            {
                Command(conn, tx, @"
INSERT INTO enc.QuestionOption (QuestionId, Role, ClientKey, Text, SortOrder) VALUES (@Q, @Role, @Key, @Text, @Sort);",
                    "@Q", questionId, "@Role", role, "@Key", options[i].Id, "@Text", options[i].Text ?? "", "@Sort", i + 1).ExecuteNonQuery();
            }
        }

        private static void InsertDependent(SqlConnection conn, SqlTransaction tx, int questionId, DependentDto dependent)
        {
            for (var i = 0; i < dependent.Levels.Count; i++)
            {
                Command(conn, tx, "INSERT INTO enc.DependentLevel (QuestionId, LevelIndex, Name) VALUES (@Q, @Level, @Name);",
                    "@Q", questionId, "@Level", i, "@Name", dependent.Levels[i].Name ?? ("Nivel " + (i + 1))).ExecuteNonQuery();
            }

            // Se insertan por nivel (padres antes que hijos); SortOrder conserva el orden original de la lista.
            var ids = new Dictionary<string, int>();
            var indexed = dependent.Options.Select((o, index) => new { Option = o, Index = index }).OrderBy(x => x.Option.Level);
            foreach (var item in indexed)
            {
                int parentId;
                object parent = null;
                if (!string.IsNullOrEmpty(item.Option.ParentId))
                {
                    if (!ids.TryGetValue(item.Option.ParentId, out parentId)) continue; // huérfana: se descarta
                    parent = parentId;
                }
                ids[item.Option.Id] = Convert.ToInt32(Command(conn, tx, @"
INSERT INTO enc.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder)
VALUES (@Q, @Parent, @Level, @Key, @Text, @Sort); SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    "@Q", questionId, "@Parent", parent, "@Level", item.Option.Level, "@Key", item.Option.Id,
                    "@Text", item.Option.Text ?? "", "@Sort", item.Index + 1).ExecuteScalar());
            }
        }

        // =================================================================
        // RESPUESTAS
        // =================================================================

        /// <summary>
        /// Guarda una respuesta (una fila por valor en QuestionResponse) e incrementa
        /// SurveyVersion.ResponseCount en la misma transacción.
        /// </summary>
        public void AddResponse(string versionId, ResponseRecordDto response, string userHash)
        {
            int vId;
            if (!TryParseKey(versionId, out vId)) throw new ArgumentException("Versión inválida.");

            using (var conn = Open())
            using (var tx = conn.BeginTransaction(IsolationLevel.ReadCommitted))
            {
                try
                {
                    // Bloquea la versión: serializa con SaveSurvey (que decide si la versión es editable).
                    Command(conn, tx, "SELECT SurveyVersionId FROM enc.SurveyVersion WITH (UPDLOCK, HOLDLOCK) WHERE SurveyVersionId = @V", "@V", vId).ExecuteScalar();

                    var map = LoadAnswerMap(conn, tx, vId);
                    var submitted = DateTime.Now;
                    DateTime parsed;
                    if (DateTime.TryParse(response.SubmittedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)) submitted = parsed;

                    var responseId = Convert.ToInt64(Command(conn, tx, @"
INSERT INTO enc.SurveyResponse (SurveyVersionId, UserHash, StartedAt, SubmittedAt) VALUES (@V, @Hash, @At, @At);
SELECT CAST(SCOPE_IDENTITY() AS BIGINT);", "@V", vId, "@Hash", userHash, "@At", submitted).ExecuteScalar());

                    foreach (var pair in response.Answers ?? new Dictionary<string, AnswerDto>())
                    {
                        AnswerQuestion q;
                        if (pair.Value == null || !map.TryGetValue(pair.Key, out q)) continue; // pregunta desconocida: se ignora
                        InsertAnswer(conn, tx, responseId, q, pair.Value);
                    }

                    Command(conn, tx, "UPDATE enc.SurveyVersion SET ResponseCount = ResponseCount + 1 WHERE SurveyVersionId = @V", "@V", vId).ExecuteNonQuery();
                    tx.Commit();
                    response.Id = responseId.ToString(CultureInfo.InvariantCulture);
                }
                catch
                {
                    tx.Rollback();
                    throw;
                }
            }
        }

        private class AnswerQuestion
        {
            public int QuestionId;
            public string Type;
            public Dictionary<string, int> Options = new Dictionary<string, int>();     // opciones y columnas
            public Dictionary<string, int> Rows = new Dictionary<string, int>();        // filas de matriz
            public Dictionary<string, int> Dependent = new Dictionary<string, int>();   // opciones dependientes
        }

        private static Dictionary<string, AnswerQuestion> LoadAnswerMap(SqlConnection conn, SqlTransaction tx, int versionId)
        {
            var byKey = new Dictionary<string, AnswerQuestion>();
            var byId = new Dictionary<int, AnswerQuestion>();
            using (var r = Command(conn, tx, @"
SELECT q.QuestionId, q.ClientKey, t.Code FROM enc.Question q JOIN enc.QuestionType t ON t.QuestionTypeId = q.QuestionTypeId WHERE q.SurveyVersionId = @V;
SELECT o.QuestionId, o.Role, o.ClientKey, o.QuestionOptionId FROM enc.QuestionOption o JOIN enc.Question q ON q.QuestionId = o.QuestionId WHERE q.SurveyVersionId = @V;
SELECT d.QuestionId, d.ClientKey, d.DependentOptionId FROM enc.DependentOption d JOIN enc.Question q ON q.QuestionId = d.QuestionId WHERE q.SurveyVersionId = @V;",
                "@V", versionId).ExecuteReader())
            {
                while (r.Read())
                {
                    var q = new AnswerQuestion { QuestionId = r.GetInt32(0), Type = r.GetString(2) };
                    byKey[r.GetString(1)] = q;
                    byId[q.QuestionId] = q;
                }
                r.NextResult();
                while (r.Read())
                {
                    var q = byId[r.GetInt32(0)];
                    (r.GetByte(1) == RoleMatrixRow ? q.Rows : q.Options)[r.GetString(2)] = r.GetInt32(3);
                }
                r.NextResult();
                while (r.Read()) byId[r.GetInt32(0)].Dependent[r.GetString(1)] = r.GetInt32(2);
            }
            return byKey;
        }

        private static void InsertAnswer(SqlConnection conn, SqlTransaction tx, long responseId, AnswerQuestion q, AnswerDto a)
        {
            int id;
            switch (q.Type)
            {
                case "text":
                    if (!string.IsNullOrWhiteSpace(a.Text)) InsertAnswerRow(conn, tx, responseId, q.QuestionId, null, null, null, a.Text, null, null);
                    break;
                case "scale":
                case "number":
                    if (a.Number.HasValue) InsertAnswerRow(conn, tx, responseId, q.QuestionId, null, null, null, null, a.Number, null);
                    break;
                case "date":
                    var date = ParseDate(a.Date);
                    // La hora (cita para Outlook) va en TextValue de la misma fila: no requiere columna nueva.
                    if (date != null) InsertAnswerRow(conn, tx, responseId, q.QuestionId, null, null, null, a.Time, null, (DateTime)date);
                    break;
                case "matrix":
                    foreach (var row in a.Matrix ?? new Dictionary<string, List<string>>())
                    {
                        int rowId;
                        if (!q.Rows.TryGetValue(row.Key, out rowId)) continue;
                        foreach (var col in row.Value ?? new List<string>())
                        {
                            if (q.Options.TryGetValue(col, out id)) InsertAnswerRow(conn, tx, responseId, q.QuestionId, id, rowId, null, null, null, null);
                        }
                    }
                    break;
                case "dependent":
                    foreach (var key in a.OptionIds ?? new List<string>())
                    {
                        if (q.Dependent.TryGetValue(key, out id)) InsertAnswerRow(conn, tx, responseId, q.QuestionId, null, null, id, null, null, null);
                    }
                    break;
                default: // singleChoice, multipleChoice, dropdown, yesNo
                    foreach (var key in a.OptionIds ?? new List<string>())
                    {
                        if (key == OtherKey) InsertAnswerRow(conn, tx, responseId, q.QuestionId, null, null, null, a.OtherText ?? "", null, null);
                        else if (q.Options.TryGetValue(key, out id)) InsertAnswerRow(conn, tx, responseId, q.QuestionId, id, null, null, null, null, null);
                    }
                    break;
            }
        }

        private static void InsertAnswerRow(SqlConnection conn, SqlTransaction tx, long responseId, int questionId,
            int? optionId, int? rowId, int? dependentId, string text, decimal? number, DateTime? date)
        {
            Command(conn, tx, @"
INSERT INTO enc.QuestionResponse (SurveyResponseId, QuestionId, QuestionOptionId, MatrixRowOptionId, DependentOptionId, TextValue, NumberValue, DateValue)
VALUES (@R, @Q, @O, @Row, @D, @Text, @Number, @Date);",
                "@R", responseId, "@Q", questionId, "@O", optionId, "@Row", rowId, "@D", dependentId,
                "@Text", text, "@Number", number, "@Date", date).ExecuteNonQuery();
        }

        /// <summary>Reconstruye las respuestas en el mismo formato que envía el navegador (AnswerDto).</summary>
        public IList<ResponseRecordDto> GetResponses(string versionId)
        {
            int vId;
            if (!TryParseKey(versionId, out vId)) return new List<ResponseRecordDto>();
            using (var conn = Open())
            {
                return ReadResponses(conn, "r.SurveyVersionId = @V AND r.SubmittedAt IS NOT NULL", "@V", vId);
            }
        }

        public ResponseRecordDto FindLatestResponse(string surveyId, IList<string> questionIds, string value, out string versionId)
        {
            versionId = null;
            int sId;
            if (!TryParseKey(surveyId, out sId) || questionIds == null || questionIds.Count == 0) return null;

            var parameters = new List<object> { "@S", sId, "@Value", value };
            var keys = new List<string>();
            for (var i = 0; i < questionIds.Count; i++)
            {
                keys.Add("@K" + i);
                parameters.Add("@K" + i);
                parameters.Add(questionIds[i]);
            }

            using (var conn = Open())
            {
                long responseId;
                using (var r = Command(conn, null, @"
SELECT TOP (1) r.SurveyResponseId, r.SurveyVersionId
FROM enc.SurveyResponse r
JOIN enc.SurveyVersion v ON v.SurveyVersionId = r.SurveyVersionId
JOIN enc.QuestionResponse a ON a.SurveyResponseId = r.SurveyResponseId
JOIN enc.Question q ON q.QuestionId = a.QuestionId AND q.SurveyVersionId = r.SurveyVersionId
WHERE v.SurveyId = @S AND r.SubmittedAt IS NOT NULL
  AND q.ClientKey IN (" + string.Join(", ", keys) + @")
  AND LTRIM(RTRIM(a.TextValue)) = @Value
ORDER BY r.SubmittedAt DESC, r.SurveyResponseId DESC;", parameters.ToArray()).ExecuteReader())
                {
                    if (!r.Read()) return null;
                    responseId = r.GetInt64(0);
                    versionId = Key(r.GetInt32(1));
                }
                return ReadResponses(conn, "r.SurveyResponseId = @R", "@R", responseId).FirstOrDefault();
            }
        }

        /// <summary>Lee respuestas completas (con sus valores) que cumplan el filtro sobre enc.SurveyResponse r.</summary>
        private static List<ResponseRecordDto> ReadResponses(SqlConnection conn, string where, string name, object value)
        {
            var result = new List<ResponseRecordDto>();
            var sql = @"
SELECT r.SurveyResponseId, r.SubmittedAt, q.ClientKey AS QKey, t.Code, o.ClientKey AS OKey, mr.ClientKey AS RowKey,
       d.ClientKey AS DKey, a.TextValue, a.NumberValue, a.DateValue
FROM enc.SurveyResponse r
LEFT JOIN enc.QuestionResponse a ON a.SurveyResponseId = r.SurveyResponseId
LEFT JOIN enc.Question q ON q.QuestionId = a.QuestionId
LEFT JOIN enc.QuestionType t ON t.QuestionTypeId = q.QuestionTypeId
LEFT JOIN enc.QuestionOption o ON o.QuestionOptionId = a.QuestionOptionId
LEFT JOIN enc.QuestionOption mr ON mr.QuestionOptionId = a.MatrixRowOptionId
LEFT JOIN enc.DependentOption d ON d.DependentOptionId = a.DependentOptionId
WHERE " + where + @"
ORDER BY r.SurveyResponseId, a.QuestionResponseId;";

            using (var r = Command(conn, null, sql, name, value).ExecuteReader())
            {
                ResponseRecordDto current = null;
                long currentId = -1;
                while (r.Read())
                {
                    var responseId = r.GetInt64(0);
                    if (responseId != currentId)
                    {
                        currentId = responseId;
                        current = new ResponseRecordDto
                        {
                            Id = responseId.ToString(CultureInfo.InvariantCulture),
                            SubmittedAt = r.GetDateTime(1).ToString("s", CultureInfo.InvariantCulture),
                            Answers = new Dictionary<string, AnswerDto>()
                        };
                        result.Add(current);
                    }
                    if (r.IsDBNull(2)) continue; // respuesta sin valores

                    var qKey = r.GetString(2);
                    AnswerDto answer;
                    if (!current.Answers.TryGetValue(qKey, out answer))
                    {
                        answer = new AnswerDto();
                        current.Answers[qKey] = answer;
                    }
                    ReadAnswerValue(r, r.GetString(3), answer);
                }
            }
            return result;
        }

        private static void ReadAnswerValue(SqlDataReader r, string type, AnswerDto a)
        {
            var optionKey = Str(r, 4);
            var rowKey = Str(r, 5);
            var depKey = Str(r, 6);
            var text = Str(r, 7);

            switch (type)
            {
                case "text":
                    a.Text = text;
                    break;
                case "scale":
                case "number":
                    if (!r.IsDBNull(8)) a.Number = r.GetDecimal(8);
                    break;
                case "date":
                    if (!r.IsDBNull(9)) a.Date = r.GetDateTime(9).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                    a.Time = text;
                    break;
                case "matrix":
                    if (rowKey == null || optionKey == null) break;
                    if (a.Matrix == null) a.Matrix = new Dictionary<string, List<string>>();
                    if (!a.Matrix.ContainsKey(rowKey)) a.Matrix[rowKey] = new List<string>();
                    a.Matrix[rowKey].Add(optionKey);
                    break;
                case "dependent":
                    if (depKey == null) break;
                    if (a.OptionIds == null) a.OptionIds = new List<string>();
                    a.OptionIds.Add(depKey);
                    break;
                default:
                    if (a.OptionIds == null) a.OptionIds = new List<string>();
                    if (optionKey != null) a.OptionIds.Add(optionKey);
                    else { a.OptionIds.Add(OtherKey); a.OtherText = text; } // "Otro": sin opción y con texto
                    break;
            }
        }

        public bool HasUserResponded(string versionId, string userHash)
        {
            int vId;
            if (!TryParseKey(versionId, out vId) || string.IsNullOrEmpty(userHash)) return false;
            using (var conn = Open())
            {
                return Command(conn, null,
                    "SELECT CASE WHEN EXISTS (SELECT 1 FROM enc.SurveyResponse WHERE SurveyVersionId = @V AND UserHash = @Hash) THEN 1 ELSE 0 END",
                    "@V", vId, "@Hash", userHash).ExecuteScalar().Equals(1);
            }
        }

        // =================================================================
        // MAPEOS Y UTILIDADES
        // =================================================================

        /// <summary>Lo que no tiene columna propia se guarda en Question.SettingsJson.</summary>
        private class StoredSettings
        {
            public QuestionValidationDto Validation { get; set; }
            public string MatrixSelection { get; set; }
            public bool? DependentMultiple { get; set; }

            /// <summary>Desplegable en cascada: ClientKey de la pregunta padre.</summary>
            public string ParentQuestion { get; set; }

            /// <summary>Desplegable en cascada: ClientKey de opción -> ClientKey de la opción padre.</summary>
            public Dictionary<string, string> OptionParents { get; set; }

            public bool? Identifier { get; set; }

            /// <summary>Pregunta de fecha: ofrece la cita para Outlook.</summary>
            public bool? Outlook { get; set; }
            public string OutlookSubject { get; set; }
        }

        private static string SerializeSettings(QuestionDto q)
        {
            var cascade = !string.IsNullOrEmpty(q.ParentQuestionId);
            return Json.Serialize(new StoredSettings
            {
                Validation = q.Validation ?? new QuestionValidationDto(),
                MatrixSelection = q.Matrix != null ? q.Matrix.SelectionType : null,
                DependentMultiple = q.Dependent != null ? (bool?)q.Dependent.Multiple : null,
                ParentQuestion = cascade ? q.ParentQuestionId : null,
                OptionParents = cascade
                    ? (q.Options ?? new List<OptionDto>()).Where(o => !string.IsNullOrEmpty(o.ParentId) && o.Id != null)
                        .GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First().ParentId)
                    : null,
                Identifier = q.Identifier ? (bool?)true : null,
                Outlook = q.Outlook ? (bool?)true : null,
                OutlookSubject = q.Outlook && !string.IsNullOrWhiteSpace(q.OutlookSubject) ? q.OutlookSubject.Trim() : null
            });
        }

        /// <summary>Devuelve el mapa de opciones padre (se aplica cuando ya se leyeron las opciones).</summary>
        private static Dictionary<string, string> ApplySettings(QuestionDto q, string json)
        {
            var settings = string.IsNullOrWhiteSpace(json) ? new StoredSettings() : Json.Deserialize<StoredSettings>(json);
            q.Validation = settings.Validation ?? new QuestionValidationDto();
            if (q.Type == "matrix") EnsureMatrix(q).SelectionType = settings.MatrixSelection ?? "single";
            if (q.Type == "dependent") EnsureDependent(q).Multiple = settings.DependentMultiple ?? true;
            q.ParentQuestionId = settings.ParentQuestion;
            q.Identifier = settings.Identifier ?? false;
            q.Outlook = settings.Outlook ?? false;
            q.OutlookSubject = settings.OutlookSubject;
            return settings.OptionParents;
        }

        private static MatrixDto EnsureMatrix(QuestionDto q)
        {
            if (q.Matrix == null) q.Matrix = new MatrixDto();
            return q.Matrix;
        }

        private static DependentDto EnsureDependent(QuestionDto q)
        {
            if (q.Dependent == null) q.Dependent = new DependentDto();
            return q.Dependent;
        }

        private static List<string> AudienceList(SegmentationDto seg, string name)
        {
            switch (name)
            {
                case "roles": return seg.Roles ?? (seg.Roles = new List<string>());
                case "segments": return seg.Segments ?? (seg.Segments = new List<string>());
                case "regions": return seg.Regions ?? (seg.Regions = new List<string>());
                case "branches": return seg.Branches ?? (seg.Branches = new List<string>());
                default: return seg.Users ?? (seg.Users = new List<string>());
            }
        }

        /// <summary>Lee las columnas de SurveyVersion empezando en 'offset' (SurveyVersionId).</summary>
        private static SurveyVersionDto ReadVersionHeader(SqlDataReader r, int o)
        {
            return new SurveyVersionDto
            {
                Id = Key(r.GetInt32(o)),
                Number = r.GetInt32(o + 1),
                Status = StatusToString(r.GetByte(o + 2)),
                Settings = new PublishSettingsDto
                {
                    StartDate = r.IsDBNull(o + 3) ? null : r.GetDateTime(o + 3).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    EndDate = r.IsDBNull(o + 4) ? null : r.GetDateTime(o + 4).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    AllowMultipleResponses = r.GetBoolean(o + 5),
                    Anonymous = r.GetBoolean(o + 6),
                    RequireAuth = r.GetBoolean(o + 7),
                    WelcomeMessage = Str(r, o + 8),
                    ThankYouMessage = Str(r, o + 9)
                },
                ResponseCount = r.GetInt32(o + 10),
                CreatedAt = r.GetDateTime(o + 11),
                PublishedAt = r.IsDBNull(o + 12) ? (DateTime?)null : r.GetDateTime(o + 12),
                Segmentation = new SegmentationDto { Mode = r.GetByte(o + 13) == 1 ? "segmented" : "all" }
            };
        }

        private static Dictionary<string, int> QuestionTypeIds(SqlConnection conn, SqlTransaction tx)
        {
            lock (TypeLock)
            {
                if (_questionTypeIds != null) return _questionTypeIds;
                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                using (var r = Command(conn, tx, "SELECT Code, QuestionTypeId FROM enc.QuestionType").ExecuteReader())
                {
                    while (r.Read()) map[r.GetString(0)] = r.GetInt32(1);
                }
                _questionTypeIds = map;
                return map;
            }
        }

        private static byte AudienceMode(SurveyVersionDto v)
        {
            return (byte)(v.Segmentation != null && v.Segmentation.Mode == "segmented" ? 1 : 0);
        }

        private static byte StatusToByte(string status)
        {
            switch (status)
            {
                case SurveyStatuses.Published: return 2;
                case SurveyStatuses.Closed: return 3;
                case SurveyStatuses.Archived: return 4;
                default: return 1;
            }
        }

        private static string StatusToString(byte status)
        {
            switch (status)
            {
                case 2: return SurveyStatuses.Published;
                case 3: return SurveyStatuses.Closed;
                case 4: return SurveyStatuses.Archived;
                default: return SurveyStatuses.Draft;
            }
        }

        private static int StatusWriteOrder(string status)
        {
            if (status == SurveyStatuses.Archived || status == SurveyStatuses.Closed) return 0;
            if (status == SurveyStatuses.Draft) return 1;
            return 2;
        }

        private SqlConnection Open()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }

        /// <summary>
        /// Crea un comando con parámetros en pares nombre/valor; null se envía como DBNull.
        /// Las consultas usan el prefijo "enc."; SurveySchema lo cambia por Surveys:Schema.
        /// </summary>
        private static SqlCommand Command(SqlConnection conn, SqlTransaction tx, string sql, params object[] parameters)
        {
            var cmd = new SqlCommand(SurveySchema.Apply(sql), conn, tx);
            for (var i = 0; i + 1 < parameters.Length; i += 2)
            {
                cmd.Parameters.AddWithValue((string)parameters[i], parameters[i + 1] ?? DBNull.Value);
            }
            return cmd;
        }

        private static string Str(SqlDataReader r, int i)
        {
            return r.IsDBNull(i) ? null : r.GetString(i);
        }

        private static string Key(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }

        private static bool TryParseKey(string value, out int id)
        {
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id);
        }

        private static object ParseDate(string value)
        {
            DateTime date;
            return DateTime.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                ? (object)date
                : null;
        }

        private string CurrentUser()
        {
            if (!string.IsNullOrEmpty(_userName)) return _userName;
            var principal = System.Threading.Thread.CurrentPrincipal;
            return principal != null && principal.Identity != null && principal.Identity.IsAuthenticated
                ? principal.Identity.Name
                : "sistema";
        }
    }
}
