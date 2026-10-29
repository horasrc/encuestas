using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ModuloEncuestas.Models.Dto;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Reglas de negocio del módulo de encuestas (independientes de la BD y de MVC).
    /// El JavaScript replica algunas de estas validaciones para dar retroalimentación
    /// inmediata, pero ESTE servicio es la fuente de verdad.
    /// </summary>
    /// <summary>
    /// Contexto de la petición. Lo arma el controlador con lo que indica el portal
    /// (Infrastructure.SurveyAccess): el módulo no tiene administración de usuarios propia.
    /// </summary>
    public class SurveyServiceOptions
    {
        /// <summary>Usuario que hace la operación (para la bitácora). Null = "sistema".</summary>
        public string UserName { get; set; }

        /// <summary>Segmento del portal en que se trabaja. Null = no se trabaja por segmento.</summary>
        public string Segment { get; set; }

        /// <summary>Segmentos permitidos a la persona (si no se indicó Segment). Null = todos.</summary>
        public IList<string> AllowedSegments { get; set; }

        /// <summary>Usuarios del tramo de control: resultados solo con sus respuestas. Null = todas.</summary>
        public IList<string> Team { get; set; }
    }

    public class SurveyService
    {
        private static readonly string[] OptionTypes = { "singleChoice", "multipleChoice", "dropdown", "yesNo" };

        private readonly ISurveyRepository _repository;
        private readonly SurveyServiceOptions _options;

        public SurveyService(ISurveyRepository repository)
            : this(repository, new SurveyServiceOptions())
        {
        }

        public SurveyService(ISurveyRepository repository, SurveyServiceOptions options)
        {
            _repository = repository;
            _options = options ?? new SurveyServiceOptions();
        }

        // -----------------------------------------------------------------
        // Consulta
        // -----------------------------------------------------------------

        public SurveyDto GetSurvey(string surveyId)
        {
            return Load(surveyId);
        }

        /// <summary>
        /// ¿La encuesta está en el alcance de la petición? Con segmento: debe pertenecer a él.
        /// Sin segmento pero con segmentos permitidos: debe pertenecer a alguno (o no tener segmento).
        /// Las encuestas sin segmento (creadas antes, o fuera de un segmento) aparecen en todos.
        /// </summary>
        public bool InScope(SurveyDto survey)
        {
            if (survey == null) return false;
            var segments = survey.Versions.Where(v => v.Segmentation != null && v.Segmentation.Mode == "segmented")
                .SelectMany(v => v.Segmentation.Segments ?? new List<string>()).ToList();
            if (segments.Count == 0) return true;
            if (_options.Segment != null) return segments.Any(s => string.Equals(s, _options.Segment, StringComparison.OrdinalIgnoreCase));
            return _options.AllowedSegments == null
                || segments.Any(s => _options.AllowedSegments.Any(a => string.Equals(a, s, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>La encuesta, o null si no existe o está fuera del alcance (se trata igual que inexistente).</summary>
        private SurveyDto Load(string surveyId)
        {
            var survey = _repository.GetSurvey(surveyId);
            return InScope(survey) ? survey : null;
        }

        /// <summary>Listado para la pantalla /Encuestas (solo las del segmento).</summary>
        public List<SurveySummaryDto> GetSurveyList()
        {
            return _repository.GetSurveys().Where(InScope).Select(Summary).ToList();
        }

        /// <summary>
        /// Para quien responde: encuestas publicadas del segmento que puede contestar ahora
        /// (vigentes y sin respuesta suya, salvo que admitan varias).
        /// </summary>
        public List<SurveySummaryDto> GetPending(string userName)
        {
            var result = new List<SurveySummaryDto>();
            foreach (var s in _repository.GetSurveys().Where(InScope))
            {
                var published = s.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Published);
                string userHash;
                if (published == null || CheckEligibility(published, userName, out userHash) != null) continue;
                result.Add(Summary(s));
            }
            return result;
        }

        private static SurveySummaryDto Summary(SurveyDto s)
        {
            var current = s.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Published)
                ?? s.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Draft)
                ?? s.Versions.OrderByDescending(v => v.Number).FirstOrDefault();
            var draft = s.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Draft);
            return new SurveySummaryDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                Status = current == null ? SurveyStatuses.Draft : current.Status,
                CurrentVersion = current == null ? 0 : current.Number,
                DraftVersion = draft != null && draft != current ? (int?)draft.Number : null,
                ResponseCount = current == null ? 0 : current.ResponseCount,
                TotalResponses = s.Versions.Sum(v => v.ResponseCount),
                VersionCount = s.Versions.Count,
                StartDate = current == null ? null : current.Settings.StartDate,
                EndDate = current == null ? null : current.Settings.EndDate
            };
        }

        /// <summary>
        /// Lo que necesita quien responde: solo la versión publicada y sin datos
        /// de administración (borradores, histórico, segmentación).
        /// </summary>
        public SurveyDto GetPublished(string surveyId)
        {
            var survey = Load(surveyId);
            if (survey == null) return null;
            var published = survey.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Published);
            var result = new SurveyDto { Id = survey.Id, Name = survey.Name };
            if (published != null)
            {
                result.Versions.Add(new SurveyVersionDto
                {
                    Id = published.Id,
                    Number = published.Number,
                    Status = published.Status,
                    Settings = published.Settings,
                    Questions = published.Questions,
                    Segmentation = null // no se expone a quien responde
                });
            }
            return result;
        }

        /// <summary>Crea una encuesta nueva con su versión 1 en borrador.</summary>
        public ApiResult<SurveyDto> CreateSurvey(CreateSurveyRequest request)
        {
            var name = request == null ? null : (request.Name ?? "").Trim();
            if (string.IsNullOrEmpty(name)) return ApiResult<SurveyDto>.Fail("Escribe el nombre de la encuesta.", null);
            if (name.Length > 200) return ApiResult<SurveyDto>.Fail("El nombre admite máximo 200 caracteres.", null);

            var survey = new SurveyDto { Name = name, Description = request.Description };
            survey.Versions.Add(new SurveyVersionDto
            {
                Id = SurveyVersionCloner.NewId("v"),
                Number = 1,
                Status = SurveyStatuses.Draft,
                CreatedAt = DateTime.Now,
                // Pertenece al segmento del portal en que se creó (sin segmento = aparece en todos).
                Segmentation = SegmentationDto.For(_options.Segment),
                Settings = new PublishSettingsDto
                {
                    StartDate = DateTime.Today.ToString("yyyy-MM-dd"),
                    EndDate = DateTime.Today.AddDays(30).ToString("yyyy-MM-dd"),
                    RequireAuth = true,
                    WelcomeMessage = "",
                    ThankYouMessage = "¡Gracias por tu respuesta!"
                }
            });
            _repository.SaveSurvey(survey); // asigna el Id definitivo
            Audit(survey.Id, 1, "created", "Creó la encuesta «" + survey.Name + "».");
            return ApiResult<SurveyDto>.Success(survey, "Encuesta creada.");
        }

        /// <summary>
        /// Copia una encuesta como encuesta nueva (versión 1 en borrador) a partir de
        /// su borrador, o si no tiene, de la versión publicada o la más reciente.
        /// No copia respuestas ni histórico.
        /// </summary>
        public ApiResult<SurveyDto> DuplicateSurvey(string surveyId)
        {
            var source = Load(surveyId);
            if (source == null) return ApiResult<SurveyDto>.Fail("La encuesta no existe.", null);
            var from = source.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Draft)
                ?? source.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Published)
                ?? source.Versions.OrderByDescending(v => v.Number).FirstOrDefault();
            if (from == null) return ApiResult<SurveyDto>.Fail("La encuesta no tiene versiones que copiar.", null);

            var name = "Copia de " + source.Name;
            var copy = new SurveyDto { Name = name.Length > 200 ? name.Substring(0, 200) : name, Description = source.Description };
            copy.Versions.Add(SurveyVersionCloner.Clone(from, 1));
            if (_options.Segment != null) copy.Versions[0].Segmentation = SegmentationDto.For(_options.Segment); // la copia queda en el segmento actual
            _repository.SaveSurvey(copy);
            Audit(copy.Id, 1, "duplicated", "Creó la encuesta como copia de «" + source.Name + "» (versión " + from.Number + ").");
            return ApiResult<SurveyDto>.Success(copy, "Encuesta duplicada como borrador.");
        }

        /// <summary>Elimina (baja lógica) una encuesta sin respuestas. Con respuestas, se debe archivar.</summary>
        public ApiResult<string> DeleteSurvey(string surveyId)
        {
            var survey = Load(surveyId);
            if (survey == null) return ApiResult<string>.Fail("La encuesta no existe.", null);
            if (survey.Versions.Any(v => v.ResponseCount > 0))
                return ApiResult<string>.Fail("La encuesta tiene respuestas y no puede eliminarse. Ciérrala o archívala para conservar el histórico.", null);

            _repository.DeleteSurvey(survey.Id);
            Audit(survey.Id, null, "deleted", "Eliminó la encuesta «" + survey.Name + "».");
            return ApiResult<string>.Success(survey.Id, "Encuesta eliminada.");
        }

        /// <summary>
        /// Una versión se puede modificar directamente si es borrador, o si está
        /// publicada pero todavía no tiene respuestas.
        /// </summary>
        public static bool IsEditable(SurveyVersionDto version)
        {
            return version.Status == SurveyStatuses.Draft
                || (version.Status == SurveyStatuses.Published && version.ResponseCount == 0);
        }

        // -----------------------------------------------------------------
        // Guardar borrador (con regla de versionado)
        // -----------------------------------------------------------------

        public ApiResult<SurveyDto> SaveDraft(SaveDraftRequest request)
        {
            var survey = Load(request.SurveyId);
            if (survey == null) return ApiResult<SurveyDto>.Fail("La encuesta no existe.", null);
            if (request.Version == null) return ApiResult<SurveyDto>.Fail("No se recibió la versión a guardar.", null);

            if (!string.IsNullOrWhiteSpace(request.Name)) survey.Name = request.Name.Trim();

            var existing = survey.Versions.FirstOrDefault(v => v.Id == request.Version.Id);
            if (existing == null) return ApiResult<SurveyDto>.Fail("La versión no existe.", null);

            if (!IsEditable(existing))
            {
                // REGLA DE VERSIONADO: una versión publicada con respuestas no se
                // sobrescribe. Los cambios se guardan como una versión nueva.
                var draft = survey.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Draft);
                if (draft != null)
                {
                    return ApiResult<SurveyDto>.Fail(
                        string.Format("La versión {0} tiene respuestas y no puede modificarse. Ya existe el borrador v{1}; edítalo en su lugar.", existing.Number, draft.Number), null);
                }

                var newVersion = SurveyVersionCloner.Clone(request.Version, NextNumber(survey));
                newVersion.Segmentation = SurveyVersionCloner.CloneSegmentation(existing.Segmentation); // el segmento no lo cambia el navegador
                survey.Versions.Add(newVersion);
                _repository.SaveSurvey(survey);
                Audit(survey.Id, newVersion.Number, "newVersion",
                    "Guardó cambios sobre la versión " + existing.Number + " (con respuestas); se creó la versión " + newVersion.Number + ".");
                return ApiResult<SurveyDto>.Success(survey,
                    string.Format("La versión {0} tiene respuestas; los cambios se guardaron como versión {1} (borrador).", existing.Number, newVersion.Number));
            }

            // Se reemplaza el contenido pero se respetan los campos controlados por el servidor
            // (estado, respuestas y segmentos del portal).
            existing.Settings = request.Version.Settings ?? new PublishSettingsDto();
            existing.Questions = Normalize(request.Version.Questions);

            _repository.SaveSurvey(survey);
            Audit(survey.Id, existing.Number, "saved",
                "Guardó la versión " + existing.Number + " (" + existing.Questions.Count + " preguntas).");
            return ApiResult<SurveyDto>.Success(survey, "Borrador guardado.");
        }

        // -----------------------------------------------------------------
        // Publicar
        // -----------------------------------------------------------------

        public ApiResult<SurveyDto> Publish(VersionActionRequest request)
        {
            var survey = Load(request.SurveyId);
            if (survey == null) return ApiResult<SurveyDto>.Fail("La encuesta no existe.", null);

            var version = survey.Versions.FirstOrDefault(v => v.Id == request.VersionId);
            if (version == null) return ApiResult<SurveyDto>.Fail("La versión no existe.", null);
            if (version.Status != SurveyStatuses.Draft)
                return ApiResult<SurveyDto>.Fail("Solo se puede publicar una versión en borrador.", null);

            var errors = ValidateForPublish(version);
            if (errors.Count > 0) return ApiResult<SurveyDto>.Fail("La encuesta tiene errores.", errors);

            // La versión publicada anterior pasa a histórico (Archivada); sus respuestas se conservan.
            var archived = survey.Versions.Where(v => v.Status == SurveyStatuses.Published).ToList();
            foreach (var previous in archived)
            {
                previous.Status = SurveyStatuses.Archived;
            }

            version.Status = SurveyStatuses.Published;
            version.PublishedAt = DateTime.Now;
            _repository.SaveSurvey(survey);

            foreach (var previous in archived)
            {
                Audit(survey.Id, previous.Number, "archived", "La versión " + previous.Number + " pasó al histórico al publicar la versión " + version.Number + ".");
            }
            Audit(survey.Id, version.Number, "published", "Publicó la versión " + version.Number + " (vigencia " +
                (version.Settings.StartDate ?? "?") + " a " + (version.Settings.EndDate ?? "?") + ").");

            return ApiResult<SurveyDto>.Success(survey, string.Format("Versión {0} publicada.", version.Number));
        }

        /// <summary>Validaciones mínimas antes de publicar. Devuelve mensajes legibles.</summary>
        public List<string> ValidateForPublish(SurveyVersionDto version)
        {
            var errors = new List<string>();
            var questions = version.Questions.OrderBy(q => q.Order).ToList();
            var ids = new HashSet<string>(questions.Select(q => q.Id));

            if (questions.Count == 0) errors.Add("La encuesta no tiene preguntas.");

            for (var i = 0; i < questions.Count; i++)
            {
                var q = questions[i];
                var label = "P" + (i + 1);

                if (string.IsNullOrWhiteSpace(q.Text)) errors.Add(label + ": falta el texto de la pregunta.");

                if (OptionTypes.Contains(q.Type) && q.Options.Count(o => !string.IsNullOrWhiteSpace(o.Text)) < 2)
                    errors.Add(label + ": requiere al menos 2 opciones.");

                if (q.Type == "matrix" && (q.Matrix == null || q.Matrix.Rows.Count == 0 || q.Matrix.Columns.Count < 2))
                    errors.Add(label + ": la matriz requiere filas y al menos 2 columnas.");

                if (q.Type == "dependent" && (q.Dependent == null || !q.Dependent.Options.Any(o => o.Level == 0)))
                    errors.Add(label + ": la lista dependiente no tiene opciones de primer nivel.");

                var v = q.Validation ?? new QuestionValidationDto();
                if (v.MinLength.HasValue && v.MaxLength.HasValue && v.MinLength > v.MaxLength)
                    errors.Add(label + ": el mínimo de caracteres es mayor que el máximo.");
                if (v.MinSelections.HasValue && v.MaxSelections.HasValue && v.MinSelections > v.MaxSelections)
                    errors.Add(label + ": el mínimo de selecciones es mayor que el máximo.");
                if (v.MinValue.HasValue && v.MaxValue.HasValue && v.MinValue > v.MaxValue)
                    errors.Add(label + ": el valor mínimo es mayor que el máximo.");

                foreach (var r in q.Rules)
                {
                    var needsTarget = r.Action != "end";
                    if (needsTarget && (string.IsNullOrEmpty(r.TargetQuestionId) || !ids.Contains(r.TargetQuestionId)))
                        errors.Add(label + ": una regla condicional apunta a una pregunta que ya no existe.");
                }

                if (q.Type == "dropdown" && !string.IsNullOrEmpty(q.ParentQuestionId))
                {
                    var p = questions.FindIndex(x => x.Id == q.ParentQuestionId);
                    if (p < 0) errors.Add(label + ": depende de una pregunta que ya no existe.");
                    else if (p > i) errors.Add(label + ": depende de P" + (p + 1) + ", que está después; muévela antes.");
                    else
                    {
                        var parentOptions = new HashSet<string>(questions[p].Options.Select(o => o.Id));
                        if (q.Options.Any(o => !string.IsNullOrEmpty(o.ParentId) && !parentOptions.Contains(o.ParentId)))
                            errors.Add(label + ": una opción depende de una opción que ya no existe en P" + (p + 1) + ".");
                    }
                }
            }

            if (questions.Count(q => q.Identifier && q.Type == "text") > 1)
                errors.Add("Solo puede haber una pregunta marcada como identificador.");

            var s = version.Settings ?? new PublishSettingsDto();
            DateTime start, end;
            if (TryParseDate(s.StartDate, out start) && TryParseDate(s.EndDate, out end) && end < start)
                errors.Add("Configuración: la fecha de cierre es anterior a la fecha de inicio.");

            return errors;
        }

        // -----------------------------------------------------------------
        // Nueva versión / cambio de estado
        // -----------------------------------------------------------------

        public ApiResult<SurveyDto> CreateVersion(VersionActionRequest request)
        {
            var survey = Load(request.SurveyId);
            if (survey == null) return ApiResult<SurveyDto>.Fail("La encuesta no existe.", null);

            var existingDraft = survey.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Draft);
            if (existingDraft != null)
                return ApiResult<SurveyDto>.Success(survey, string.Format("Ya existe el borrador v{0}.", existingDraft.Number));

            var source = survey.Versions.FirstOrDefault(v => v.Id == request.VersionId);
            if (source == null) return ApiResult<SurveyDto>.Fail("La versión origen no existe.", null);

            var copy = SurveyVersionCloner.Clone(source, NextNumber(survey));
            survey.Versions.Add(copy);
            _repository.SaveSurvey(survey);
            Audit(survey.Id, copy.Number, "newVersion", "Creó la versión " + copy.Number + " a partir de la versión " + source.Number + ".");
            return ApiResult<SurveyDto>.Success(survey, string.Format("Se creó la versión {0} (borrador).", copy.Number));
        }

        public ApiResult<SurveyDto> ChangeStatus(VersionActionRequest request)
        {
            var survey = Load(request.SurveyId);
            if (survey == null) return ApiResult<SurveyDto>.Fail("La encuesta no existe.", null);
            var version = survey.Versions.FirstOrDefault(v => v.Id == request.VersionId);
            if (version == null) return ApiResult<SurveyDto>.Fail("La versión no existe.", null);

            if (request.Status == SurveyStatuses.Closed && version.Status != SurveyStatuses.Published)
                return ApiResult<SurveyDto>.Fail("Solo se puede cerrar una versión publicada.", null);
            if (request.Status != SurveyStatuses.Closed && request.Status != SurveyStatuses.Archived)
                return ApiResult<SurveyDto>.Fail("Estado no permitido.", null);

            version.Status = request.Status;
            _repository.SaveSurvey(survey);
            var closed = request.Status == SurveyStatuses.Closed;
            Audit(survey.Id, version.Number, closed ? "closed" : "archived",
                (closed ? "Cerró" : "Archivó") + " la versión " + version.Number + " (" + version.ResponseCount + " respuestas).");
            return ApiResult<SurveyDto>.Success(survey, "Estado actualizado.");
        }

        // -----------------------------------------------------------------
        // Respuestas y resultados
        // -----------------------------------------------------------------

        /// <summary>
        /// ¿Puede este usuario responder ahora? La página del colaborador lo consulta
        /// antes de mostrar la encuesta; Submit vuelve a aplicar las mismas reglas.
        /// </summary>
        public EligibilityDto GetEligibility(string surveyId, string userName)
        {
            var survey = Load(surveyId);
            var version = survey == null ? null : survey.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Published);
            if (version == null) return EligibilityDto.Denied("unavailable", "Esta encuesta no está abierta en este momento.");
            string userHash;
            return CheckEligibility(version, userName, out userHash) ?? new EligibilityDto { CanRespond = true, VersionId = version.Id };
        }

        /// <summary>Reglas para responder: publicada, vigente, autenticación y una respuesta por persona.</summary>
        private EligibilityDto CheckEligibility(SurveyVersionDto version, string userName, out string userHash)
        {
            userHash = string.IsNullOrEmpty(userName) ? null : Hash(userName);
            if (version.Status != SurveyStatuses.Published)
                return EligibilityDto.Denied("unavailable", "Esta encuesta no está abierta en este momento.");

            var today = DateTime.Today;
            DateTime start, end;
            if (TryParseDate(version.Settings.StartDate, out start) && today < start)
                return EligibilityDto.Denied("notStarted", "La encuesta estará disponible a partir del " + start.ToString("dd/MM/yyyy") + ".");
            if (TryParseDate(version.Settings.EndDate, out end) && today > end)
                return EligibilityDto.Denied("ended", "El periodo para responder terminó el " + end.ToString("dd/MM/yyyy") + ".");

            if (version.Settings.RequireAuth && string.IsNullOrEmpty(userName))
                return EligibilityDto.Denied("authRequired", "Debes iniciar sesión para responder.");

            if (!version.Settings.AllowMultipleResponses && userHash != null && _repository.HasUserResponded(version.Id, userHash))
                return EligibilityDto.Denied("alreadyResponded", "Tu respuesta ya quedó registrada. ¡Gracias por participar!");

            // El segmento ya se validó al cargar la encuesta (Load / InScope): fuera de él, "no existe".
            return null;
        }

        public ApiResult<string> Submit(SubmitResponseDto request, string userName)
        {
            var survey = Load(request.SurveyId);
            var version = survey == null ? null : survey.Versions.FirstOrDefault(v => v.Id == request.VersionId);
            if (version == null) return ApiResult<string>.Fail("La encuesta no existe.", null);

            string userHash;
            var denied = CheckEligibility(version, userName, out userHash);
            if (denied != null) return ApiResult<string>.Fail(denied.Reason, null);

            // La lógica condicional se recalcula aquí: solo se exigen y guardan las preguntas visibles.
            Dictionary<string, AnswerDto> accepted;
            var errors = SurveyLogic.ValidateSubmission(version, request.Answers, out accepted);
            if (errors.Count > 0) return ApiResult<string>.Fail("Revisa tus respuestas.", errors);

            var record = new ResponseRecordDto
            {
                Id = SurveyVersionCloner.NewId("r"),
                SubmittedAt = DateTime.Now.ToString("s", CultureInfo.InvariantCulture),
                Answers = accepted
            };
            // El repositorio guarda la respuesta e incrementa ResponseCount en la misma operación.
            _repository.AddResponse(version.Id, record, userHash);
            return ApiResult<string>.Success(record.Id, version.Settings.ThankYouMessage);
        }

        /// <summary>
        /// Busca la última respuesta capturada (en cualquier versión) cuya pregunta identificador
        /// tenga ese valor (ej. número de cliente) y la devuelve con los IDs de la versión publicada,
        /// para precargar el formulario. Solo para quien puede responder la encuesta ahora.
        /// </summary>
        public LastResponseDto GetLastResponse(string surveyId, string value, string userName)
        {
            var survey = Load(surveyId);
            var version = survey == null ? null : survey.Versions.FirstOrDefault(v => v.Status == SurveyStatuses.Published);
            if (version == null) return new LastResponseDto { Message = "Esta encuesta no está abierta en este momento." };

            string userHash;
            var denied = CheckEligibility(version, userName, out userHash);
            if (denied != null) return new LastResponseDto { Message = denied.Reason };

            var key = IdentifierOf(version);
            if (key == null) return new LastResponseDto { Message = "La encuesta no tiene una pregunta identificador." };
            value = (value ?? "").Trim();
            if (value.Length == 0) return new LastResponseDto { Message = "Escribe el valor a buscar." };

            // La pregunta identificador de cada versión (las versiones nuevas tienen IDs nuevos).
            var keys = survey.Versions.Select(IdentifierOf).Where(k => k != null).Select(k => k.Id).Distinct().ToList();
            string fromVersionId;
            var record = _repository.FindLatestResponse(survey.Id, keys, value, out fromVersionId);
            if (record == null) return new LastResponseDto { Message = "No hay capturas anteriores para «" + value + "»." };

            var from = survey.Versions.First(v => v.Id == fromVersionId);
            return new LastResponseDto
            {
                Found = true,
                SubmittedAt = record.SubmittedAt,
                VersionNumber = from.Number,
                Answers = MapAnswers(from, version, record.Answers ?? new Dictionary<string, AnswerDto>()),
                Message = "Se cargó la captura del " + FormatSubmitted(record.SubmittedAt) + "."
            };
        }

        private static QuestionDto IdentifierOf(SurveyVersionDto version)
        {
            return version.Questions.OrderBy(q => q.Order).FirstOrDefault(q => q.Identifier && q.Type == "text");
        }

        private static string FormatSubmitted(string iso)
        {
            DateTime at;
            return DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out at) ? at.ToString("dd/MM/yyyy HH:mm") : iso;
        }

        /// <summary>
        /// Traduce respuestas de una versión a otra. Las preguntas se emparejan por tipo y texto,
        /// y las opciones, filas y columnas por su texto: lo que ya no existe se omite.
        /// </summary>
        public static Dictionary<string, AnswerDto> MapAnswers(SurveyVersionDto from, SurveyVersionDto to, IDictionary<string, AnswerDto> answers)
        {
            var result = new Dictionary<string, AnswerDto>();
            var used = new HashSet<string>();
            foreach (var target in to.Questions.OrderBy(q => q.Order))
            {
                var source = from.Id == to.Id
                    ? target
                    : from.Questions.OrderBy(q => q.Order).FirstOrDefault(q => !used.Contains(q.Id) && q.Type == target.Type && SameText(q.Text, target.Text));
                AnswerDto a;
                if (source == null || !answers.TryGetValue(source.Id, out a) || a == null) continue;
                used.Add(source.Id);

                var mapped = MapAnswer(source, target, a);
                if (!SurveyLogic.IsEmpty(mapped)) result[target.Id] = mapped;
            }
            return result;
        }

        private static AnswerDto MapAnswer(QuestionDto source, QuestionDto target, AnswerDto a)
        {
            var result = new AnswerDto { Text = a.Text, Number = a.Number, Date = a.Date, Time = target.Outlook ? a.Time : null };
            if (a.OptionIds != null)
            {
                result.OptionIds = new List<string>();
                foreach (var id in a.OptionIds)
                {
                    string mapped = null;
                    if (id == SurveyLogic.OtherOption) mapped = target.AllowOther ? id : null;
                    else if (target.Type == "dependent") mapped = MapDependent(source.Dependent, target.Dependent, id);
                    else mapped = MapOption(source.Options, target.Options, id);
                    if (mapped != null && !result.OptionIds.Contains(mapped)) result.OptionIds.Add(mapped);
                }
                if (result.OptionIds.Contains(SurveyLogic.OtherOption)) result.OtherText = a.OtherText;
            }
            if (a.Matrix != null && source.Matrix != null && target.Matrix != null)
            {
                result.Matrix = new Dictionary<string, List<string>>();
                foreach (var row in a.Matrix)
                {
                    var rowId = MapOption(source.Matrix.Rows, target.Matrix.Rows, row.Key);
                    if (rowId == null || row.Value == null) continue;
                    var cols = row.Value.Select(c => MapOption(source.Matrix.Columns, target.Matrix.Columns, c)).Where(c => c != null).Distinct().ToList();
                    if (cols.Count > 0) result.Matrix[rowId] = cols;
                }
            }
            return result;
        }

        private static string MapOption(IList<OptionDto> from, IList<OptionDto> to, string id)
        {
            if (from == null || to == null) return null;
            if (from == to) return to.Any(o => o.Id == id) ? id : null;
            var option = from.FirstOrDefault(o => o.Id == id);
            var match = option == null ? null : to.FirstOrDefault(o => SameText(o.Text, option.Text));
            return match == null ? null : match.Id;
        }

        private static string MapDependent(DependentDto from, DependentDto to, string id)
        {
            if (from == null || to == null) return null;
            if (from == to) return to.Options.Any(o => o.Id == id) ? id : null;
            var option = from.Options.FirstOrDefault(o => o.Id == id);
            var match = option == null ? null : to.Options.FirstOrDefault(o => o.Level == option.Level && SameText(o.Text, option.Text));
            return match == null ? null : match.Id;
        }

        private static bool SameText(string a, string b)
        {
            return string.Equals((a ?? "").Trim(), (b ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase);
        }

        public ResultsDto GetResults(string surveyId, string versionId)
        {
            var survey = Load(surveyId);
            var version = survey == null ? null : survey.Versions.FirstOrDefault(v => v.Id == versionId);
            if (version == null) return null;

            // Tramo de control: solo las respuestas de su gente. Se compara por hash (SurveyResponse
            // no guarda el usuario), así funciona también en encuestas anónimas.
            var responses = _repository.GetResponses(version.Id).AsEnumerable();
            if (_options.Team != null)
            {
                var team = new HashSet<string>(_options.Team.Where(u => !string.IsNullOrWhiteSpace(u)).Select(u => Hash(u.Trim())));
                responses = responses.Where(r => r.UserHash != null && team.Contains(r.UserHash.Trim()));
            }

            return new ResultsDto
            {
                VersionId = version.Id,
                Audience = _options.Team == null ? 0 : _options.Team.Count,
                StartDate = version.Settings.StartDate,
                EndDate = version.Settings.EndDate,
                Responses = responses.ToList()
            };
        }

        /// <summary>Historial de la encuesta para el panel de versiones.</summary>
        public IList<AuditEntryDto> GetHistory(string surveyId)
        {
            var survey = Load(surveyId);
            return survey == null ? new List<AuditEntryDto>() : _repository.GetAudit(survey.Id, 100);
        }

        // -----------------------------------------------------------------
        // Utilidades
        // -----------------------------------------------------------------

        /// <summary>
        /// Registra en la bitácora. Un fallo al registrar no debe deshacer la operación
        /// principal (ya se guardó), así que se ignora y se reporta en el registro de eventos.
        /// </summary>
        private void Audit(string surveyId, int? versionNumber, string action, string detail)
        {
            try
            {
                _repository.AddAudit(new AuditEntryDto
                {
                    SurveyId = surveyId,
                    VersionNumber = versionNumber,
                    Action = action,
                    Detail = detail,
                    UserName = string.IsNullOrEmpty(_options.UserName) ? "sistema" : _options.UserName,
                    CreatedAt = DateTime.Now
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("No se pudo registrar en la bitácora de encuestas: " + ex.Message);
            }
        }

        private static int NextNumber(SurveyDto survey)
        {
            return survey.Versions.Count == 0 ? 1 : survey.Versions.Max(v => v.Number) + 1;
        }

        private static List<QuestionDto> Normalize(List<QuestionDto> questions)
        {
            var list = (questions ?? new List<QuestionDto>()).OrderBy(q => q.Order).ToList();
            for (var i = 0; i < list.Count; i++) list[i].Order = i + 1;
            return list;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        private static string Hash(string value)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
                return BitConverter.ToString(bytes).Replace("-", "");
            }
        }
    }
}
