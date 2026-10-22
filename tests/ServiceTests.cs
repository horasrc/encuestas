using System;
using System.Collections.Generic;
using System.Linq;
using ModuloEncuestas.Models.Dto;
using ModuloEncuestas.Services;

class TestCatalogs : IOrgCatalogProvider
{
    public OrgCatalogs GetCatalogs()
    {
        var c = new DemoOrgCatalogProvider().GetCatalogs();
        var copy = new OrgCatalogs { Roles = c.Roles, Segments = c.Segments, Regions = c.Regions, Branches = c.Branches };
        copy.Users.Add(new UserItem { Id = "u1", Login = "DOM\\gerente", Name = "Gerente", RoleId = "gerentes", BranchId = "b01" });
        copy.Users.Add(new UserItem { Id = "u2", Login = "DOM\\ejecutivo", Name = "Ejecutivo", RoleId = "ejecutivos", BranchId = "b01" });
        return copy;
    }
}

class ServiceTest
{
    static int failures = 0;
    static void Check(string name, bool ok, string detail = "")
    {
        Console.WriteLine((ok ? "OK   " : "FAIL ") + name + (ok ? "" : "  -> " + detail));
        if (!ok) failures++;
    }

    static OptionDto O(string id, string t) { return new OptionDto { Id = id, Text = t }; }
    static AnswerDto Opt(params string[] ids) { return new AnswerDto { OptionIds = ids.ToList() }; }

    static void Main()
    {
        var catalogs = new DemoOrgCatalogProvider();
        var service = new SurveyService(new InMemorySurveyRepository(catalogs));

        // 1. Crear
        var created = service.CreateSurvey(new CreateSurveyRequest { Name = "Prueba lógica" });
        Check("CreateSurvey asigna Id", created.Ok && !string.IsNullOrEmpty(created.Data.Id), created.Message);
        Check("CreateSurvey rechaza nombre vacío", !service.CreateSurvey(new CreateSurveyRequest { Name = "  " }).Ok);
        var survey = created.Data;
        var v = survey.Versions[0];

        // 2. Contenido: P1 obligatoria, P2 condicional (Sí->P3, No aplica->fin), P3 obligatoria, P4 matriz obligatoria, P5 múltiple con Otro
        v.Questions = new List<QuestionDto>
        {
            new QuestionDto { Id = "q1", Order = 1, Type = "singleChoice", Text = "Utilidad", Required = true, Options = { O("a", "Útil"), O("b", "Poco") } },
            new QuestionDto { Id = "q2", Order = 2, Type = "singleChoice", Text = "¿Usas?", Required = true, Options = { O("si", "Sí"), O("no", "No"), O("na", "No aplica") },
                Rules = { new RuleDto { Id = "r1", Operator = "equals", Value = "si", Action = "show", TargetQuestionId = "q3" },
                          new RuleDto { Id = "r2", Operator = "equals", Value = "na", Action = "end" } } },
            new QuestionDto { Id = "q3", Order = 3, Type = "dropdown", Text = "Módulo", Required = true, Options = { O("m1", "M1"), O("m2", "M2") } },
            new QuestionDto { Id = "q4", Order = 4, Type = "matrix", Text = "Evalúa", Required = true,
                Matrix = new MatrixDto { Rows = { O("r1", "Rapidez"), O("r2", "Soporte") }, Columns = { O("c1", "Mal"), O("c2", "Bien") } } },
            new QuestionDto { Id = "q5", Order = 5, Type = "multipleChoice", Text = "Materiales", AllowOther = true, Options = { O("x", "X"), O("y", "Y") },
                Validation = new QuestionValidationDto { MaxSelections = 2 } }
        };
        var saved = service.SaveDraft(new SaveDraftRequest { SurveyId = survey.Id, Version = v });
        Check("SaveDraft", saved.Ok, saved.Message);
        var pub = service.Publish(new VersionActionRequest { SurveyId = survey.Id, VersionId = v.Id });
        Check("Publish", pub.Ok, pub.Message + " " + string.Join("; ", pub.Errors));

        Func<Dictionary<string, AnswerDto>, ApiResult<string>> submit = answers =>
            service.Submit(new SubmitResponseDto { SurveyId = survey.Id, VersionId = v.Id, Answers = answers }, "DOM\\usuario" + Guid.NewGuid());

        // 3. "No aplica" termina: solo P1 y P2 exigidas
        var r = submit(new Dictionary<string, AnswerDto> { { "q1", Opt("a") }, { "q2", Opt("na") } });
        Check("No aplica finaliza (P4 obligatoria no se exige)", r.Ok, string.Join("; ", r.Errors));

        // 4. "Sí" sin P3 -> error en P3
        r = submit(new Dictionary<string, AnswerDto> { { "q1", Opt("a") }, { "q2", Opt("si") },
            { "q4", new AnswerDto { Matrix = new Dictionary<string, List<string>> { { "r1", new List<string> { "c1" } }, { "r2", new List<string> { "c2" } } } } } });
        Check("Sí exige P3", !r.Ok && r.Errors.Any(e => e.StartsWith("P3")), string.Join("; ", r.Errors));

        // 5. "No": P3 oculta; su respuesta se descarta. Matriz incompleta -> error
        r = submit(new Dictionary<string, AnswerDto> { { "q1", Opt("a") }, { "q2", Opt("no") }, { "q3", Opt("m1") },
            { "q4", new AnswerDto { Matrix = new Dictionary<string, List<string>> { { "r1", new List<string> { "c1" } } } } } });
        Check("Matriz incompleta rechazada", !r.Ok && r.Errors.Any(e => e.StartsWith("P4")), string.Join("; ", r.Errors));

        r = submit(new Dictionary<string, AnswerDto> { { "q1", Opt("a") }, { "q2", Opt("no") }, { "q3", Opt("m1") },
            { "q4", new AnswerDto { Matrix = new Dictionary<string, List<string>> { { "r1", new List<string> { "c1" } }, { "r2", new List<string> { "c2", "zz" } } } } } });
        Check("No: envío válido (columna inexistente depurada)", r.Ok, string.Join("; ", r.Errors));
        var results = service.GetResults(survey.Id, v.Id);
        var last = results.Responses.Last();
        Check("Respuesta de P3 oculta no se guarda", !last.Answers.ContainsKey("q3"));
        Check("Columna inexistente eliminada", last.Answers["q4"].Matrix["r2"].SequenceEqual(new[] { "c2" }));

        // 6. Opción inexistente -> se depura -> obligatoria vacía
        r = submit(new Dictionary<string, AnswerDto> { { "q1", Opt("hack") }, { "q2", Opt("na") } });
        Check("Opción inexistente => obligatoria", !r.Ok && r.Errors.Any(e => e.StartsWith("P1")), string.Join("; ", r.Errors));

        // 7. Otro sin texto y máximo de selecciones
        r = submit(new Dictionary<string, AnswerDto> { { "q1", Opt("a") }, { "q2", Opt("no") },
            { "q4", new AnswerDto { Matrix = new Dictionary<string, List<string>> { { "r1", new List<string> { "c1" } }, { "r2", new List<string> { "c1" } } } } },
            { "q5", Opt("x", SurveyLogic.OtherOption) } });
        Check("Otro sin texto rechazado", !r.Ok && r.Errors.Any(e => e.StartsWith("P5")), string.Join("; ", r.Errors));
        r = submit(new Dictionary<string, AnswerDto> { { "q1", Opt("a", "b") }, { "q2", Opt("na") } });
        Check("Opción única con 2 opciones rechazada", !r.Ok && r.Errors.Any(e => e.StartsWith("P1")), string.Join("; ", r.Errors));

        // 8. Una respuesta por usuario
        var once = new Dictionary<string, AnswerDto> { { "q1", Opt("a") }, { "q2", Opt("na") } };
        var r1 = service.Submit(new SubmitResponseDto { SurveyId = survey.Id, VersionId = v.Id, Answers = once }, "DOM\\repetido");
        var r2 = service.Submit(new SubmitResponseDto { SurveyId = survey.Id, VersionId = v.Id, Answers = once }, "DOM\\repetido");
        Check("Una respuesta por usuario", r1.Ok && !r2.Ok, r2.Message);

        // 9. Listado y versión pública
        var row = service.GetSurveyList().First(s => s.Id == survey.Id);
        Check("Listado: estado y respuestas", row.Status == "Published" && row.ResponseCount == 3, row.Status + " " + row.ResponseCount);
        var published = service.GetPublished(survey.Id);
        Check("Published: solo la versión publicada, sin segmentación", published.Versions.Count == 1 && published.Versions[0].Segmentation == null);

        // 10. Audiencia
        var seg = new SegmentationDto { Mode = "segmented", Roles = { "gerentes" } };
        Check("Audiencia: usuario desconocido fuera", !service.IsInAudience(seg, "DOM\\nadie"));
        Check("Audiencia: modo todos", service.IsInAudience(new SegmentationDto(), "DOM\\nadie"));

        // 11. Elegibilidad
        var el = service.GetEligibility(survey.Id, "DOM\\repetido");
        Check("Elegibilidad: ya respondió", !el.CanRespond && el.Code == "alreadyResponded", el.Code);
        el = service.GetEligibility(survey.Id, "DOM\\nuevo");
        Check("Elegibilidad: usuario nuevo puede", el.CanRespond && el.VersionId == v.Id, el.Code);
        el = service.GetEligibility(survey.Id, null);
        Check("Elegibilidad: requiere autenticación", !el.CanRespond && el.Code == "authRequired", el.Code);
        el = service.GetEligibility("999", "DOM\\x");
        Check("Elegibilidad: encuesta inexistente", !el.CanRespond && el.Code == "unavailable", el.Code);

        // 12. Audiencia con EnforceAudience y usuario del catálogo con Login
        var cats = new TestCatalogs();
        var strict = new SurveyService(new InMemorySurveyRepository(cats), new SurveyServiceOptions { EnforceAudience = true });
        var s2 = strict.CreateSurvey(new CreateSurveyRequest { Name = "Solo gerentes" }).Data;
        var v2 = s2.Versions[0];
        v2.Segmentation = new SegmentationDto { Mode = "segmented", Roles = { "gerentes" } };
        v2.Questions = new List<QuestionDto> { new QuestionDto { Id = "g1", Order = 1, Type = "yesNo", Text = "¿Ok?", Options = { O("y", "Sí"), O("n", "No") } } };
        strict.SaveDraft(new SaveDraftRequest { SurveyId = s2.Id, Version = v2 });
        strict.Publish(new VersionActionRequest { SurveyId = s2.Id, VersionId = v2.Id });
        Check("Audiencia: gerente puede", strict.GetEligibility(s2.Id, "DOM\\gerente").CanRespond);
        el = strict.GetEligibility(s2.Id, "dom\\EJECUTIVO");
        Check("Audiencia: ejecutivo fuera (login sin distinguir mayúsculas)", !el.CanRespond && el.Code == "notInAudience", el.Code);
        var sub = strict.Submit(new SubmitResponseDto { SurveyId = s2.Id, VersionId = v2.Id, Answers = new Dictionary<string, AnswerDto> { { "g1", Opt("y") } } }, "DOM\\ejecutivo");
        Check("Audiencia: Submit también rechaza", !sub.Ok, sub.Message);

        // 13. Duplicar y eliminar
        var dup = service.DuplicateSurvey(survey.Id);
        Check("Duplicar: nueva encuesta en borrador v1", dup.Ok && dup.Data.Id != survey.Id && dup.Data.Versions.Count == 1
            && dup.Data.Versions[0].Status == "Draft" && dup.Data.Versions[0].Number == 1 && dup.Data.Name.StartsWith("Copia de"), dup.Message);
        var dq = dup.Data.Versions[0].Questions.First(q => q.Rules.Count > 0);
        Check("Duplicar: reglas reapuntadas a las preguntas copiadas", dq.Rules.All(rr => rr.Action == "end" || dup.Data.Versions[0].Questions.Any(q => q.Id == rr.TargetQuestionId))
            && dq.Id != "q2");
        Check("Duplicar: sin respuestas", dup.Data.Versions[0].ResponseCount == 0);
        var del = service.DeleteSurvey(survey.Id);
        Check("Eliminar con respuestas: rechazado", !del.Ok, del.Message);
        del = service.DeleteSurvey(dup.Data.Id);
        Check("Eliminar sin respuestas: ok", del.Ok && service.GetSurvey(dup.Data.Id) == null, del.Message);

        // 14. Bitácora de cambios
        var audited = new SurveyService(new InMemorySurveyRepository(catalogs), new SurveyServiceOptions { UserName = "DOM\\admin" });
        var s3 = audited.CreateSurvey(new CreateSurveyRequest { Name = "Con bitácora" }).Data;
        var v3 = s3.Versions[0];
        v3.Questions = new List<QuestionDto> { new QuestionDto { Id = "b1", Order = 1, Type = "yesNo", Text = "¿Ok?", Options = { O("y", "Sí"), O("n", "No") } } };
        audited.SaveDraft(new SaveDraftRequest { SurveyId = s3.Id, Version = v3 });
        audited.Publish(new VersionActionRequest { SurveyId = s3.Id, VersionId = v3.Id });
        audited.ChangeStatus(new VersionActionRequest { SurveyId = s3.Id, VersionId = v3.Id, Status = "Closed" });
        var history = audited.GetHistory(s3.Id);
        var actions = string.Join(",", history.Select(h => h.Action));
        Check("Bitácora: acciones en orden (más reciente primero)", actions == "closed,published,saved,created", actions);
        Check("Bitácora: usuario registrado", history.All(h => h.UserName == "DOM\\admin"), history.First().UserName);
        Check("Bitácora: detalle legible", history.Last().Detail.Contains("Con bitácora") && history[1].Detail.StartsWith("Publicó la versión 1"), history[1].Detail);
        var anonymousService = new SurveyService(new InMemorySurveyRepository(catalogs));
        var s4 = anonymousService.CreateSurvey(new CreateSurveyRequest { Name = "Sin usuario" }).Data;
        Check("Bitácora: sin usuario => 'sistema'", anonymousService.GetHistory(s4.Id).Single().UserName == "sistema");
        Check("Bitácora: separada por encuesta", audited.GetHistory(s4.Id).Count == 1 && audited.GetHistory(s3.Id).Count == 4);

        // 15. Esquema configurable (Surveys:Schema)
        Check("Esquema: vacío = enc", SurveySchema.Validate("  ") == "enc");
        Check("Esquema: IC aceptado", SurveySchema.Validate("IC") == "IC");
        var rejected = false;
        try { SurveySchema.Validate("IC]; DROP TABLE x;--"); } catch (System.Configuration.ConfigurationErrorsException) { rejected = true; }
        Check("Esquema: nombre con caracteres raros rechazado", rejected);
        Check("Esquema: cambia solo el prefijo enc.", SurveySchema.Apply("SELECT e.encuesta FROM enc.Survey s JOIN enc.SurveyVersion v ON 1=1", "IC")
            == "SELECT e.encuesta FROM [IC].Survey s JOIN [IC].SurveyVersion v ON 1=1");
        Check("Esquema: con enc no cambia nada", SurveySchema.Apply("FROM enc.Survey", "enc") == "FROM enc.Survey");

        CascadeTests(catalogs);
        IdentifierTests(catalogs);
        CitasIcsTests();

        Console.WriteLine(failures == 0 ? "TODAS LAS PRUEBAS PASARON" : failures + " FALLAS");
    }

    static OptionDto O(string id, string t, string parent) { return new OptionDto { Id = id, Text = t, ParentId = parent }; }

    // Desplegable cuyas opciones dependen de lo elegido en otro desplegable
    static void CascadeTests(IOrgCatalogProvider catalogs)
    {
        var service = new SurveyService(new InMemorySurveyRepository(catalogs));
        var s = service.CreateSurvey(new CreateSurveyRequest { Name = "Cascada" }).Data;
        var v = s.Versions[0];
        v.Settings.AllowMultipleResponses = true;
        v.Questions = new List<QuestionDto>
        {
            new QuestionDto { Id = "banca", Order = 1, Type = "dropdown", Text = "Banca", Required = true,
                Options = { O("bp", "Personal"), O("be", "Empresarial"), O("bx", "Sin productos") } },
            new QuestionDto { Id = "prod", Order = 2, Type = "dropdown", Text = "Producto", Required = true, ParentQuestionId = "banca",
                Options = { O("tdc", "Tarjeta", "bp"), O("hip", "Hipoteca", "bp"), O("cred", "Crédito PyME", "be") } }
        };
        service.SaveDraft(new SaveDraftRequest { SurveyId = s.Id, Version = v });
        var pub = service.Publish(new VersionActionRequest { SurveyId = s.Id, VersionId = v.Id });
        Check("Cascada: publica", pub.Ok, string.Join("; ", pub.Errors));

        Func<Dictionary<string, AnswerDto>, ApiResult<string>> submit = answers =>
            service.Submit(new SubmitResponseDto { SurveyId = s.Id, VersionId = v.Id, Answers = answers }, "DOM\\u");
        var r = submit(new Dictionary<string, AnswerDto> { { "banca", Opt("bp") }, { "prod", Opt("hip") } });
        Check("Cascada: opción de la banca elegida aceptada", r.Ok, string.Join("; ", r.Errors));
        r = submit(new Dictionary<string, AnswerDto> { { "banca", Opt("bp") }, { "prod", Opt("cred") } });
        Check("Cascada: opción de otra banca descartada (obligatoria)", !r.Ok && r.Errors.Any(e => e.StartsWith("P2")), string.Join("; ", r.Errors));
        r = submit(new Dictionary<string, AnswerDto> { { "banca", Opt("bx") } });
        Check("Cascada: sin opciones para lo elegido no se exige", r.Ok, string.Join("; ", r.Errors));
        r = submit(new Dictionary<string, AnswerDto> { { "prod", Opt("tdc") } });
        Check("Cascada: sin padre no hay opciones", !r.Ok && r.Errors.Any(e => e.StartsWith("P1")) && !r.Errors.Any(e => e.StartsWith("P2")), string.Join("; ", r.Errors));

        var copy = SurveyVersionCloner.Clone(v, 2);
        var parent = copy.Questions.First(q => q.Text == "Banca");
        var child = copy.Questions.First(q => q.Text == "Producto");
        Check("Cascada: nueva versión reapunta pregunta y opciones padre",
            child.ParentQuestionId == parent.Id && child.Options.All(o => parent.Options.Any(p => p.Id == o.ParentId)));

        child.Order = 1; parent.Order = 2;
        var errors = service.ValidateForPublish(copy);
        Check("Cascada: padre después del hijo no se publica", errors.Any(e => e.Contains("está después")), string.Join("; ", errors));
        child.Order = 2; parent.Order = 1;
        parent.Options.RemoveAt(0);
        errors = service.ValidateForPublish(copy);
        Check("Cascada: opción padre eliminada no se publica", errors.Any(e => e.Contains("opción que ya no existe")), string.Join("; ", errors));
    }

    // Identificador (ej. número de cliente): recupera la última captura, aunque sea de otra versión
    static void IdentifierTests(IOrgCatalogProvider catalogs)
    {
        var service = new SurveyService(new InMemorySurveyRepository(catalogs));
        var s = service.CreateSurvey(new CreateSurveyRequest { Name = "Visitas" }).Data;
        var v = s.Versions[0];
        v.Settings.AllowMultipleResponses = true;
        v.Questions = new List<QuestionDto>
        {
            new QuestionDto { Id = "cli", Order = 1, Type = "text", Text = "Número de cliente", Required = true, Identifier = true,
                Validation = new QuestionValidationDto { TextMode = "short" } },
            new QuestionDto { Id = "int", Order = 2, Type = "singleChoice", Text = "Interés", Options = { O("alto", "Alto"), O("bajo", "Bajo") } },
            new QuestionDto { Id = "com", Order = 3, Type = "text", Text = "Comentarios" }
        };
        service.SaveDraft(new SaveDraftRequest { SurveyId = s.Id, Version = v });
        service.Publish(new VersionActionRequest { SurveyId = s.Id, VersionId = v.Id });

        Func<string, string, string, ApiResult<string>> capture = (client, interest, comment) => service.Submit(new SubmitResponseDto
        {
            SurveyId = s.Id, VersionId = v.Id,
            Answers = new Dictionary<string, AnswerDto> { { "cli", new AnswerDto { Text = client } }, { "int", Opt(interest) }, { "com", new AnswerDto { Text = comment } } }
        }, "DOM\\ejecutivo");
        capture("00123", "bajo", "primera visita");
        capture("00999", "bajo", "otro cliente");
        var c = capture("00123", "alto", "segunda visita");
        Check("Identificador: varias capturas del mismo usuario", c.Ok, c.Message);

        var last = service.GetLastResponse(s.Id, " 00123 ", "DOM\\otro");
        Check("Identificador: devuelve la última captura de ese cliente", last.Found && last.Answers["com"].Text == "segunda visita"
            && last.Answers["int"].OptionIds.SequenceEqual(new[] { "alto" }) && last.VersionNumber == 1, last.Message);
        last = service.GetLastResponse(s.Id, "123", "DOM\\otro");
        Check("Identificador: sin coincidencia exacta no encuentra", !last.Found && last.Message.Contains("No hay capturas"), last.Message);
        last = service.GetLastResponse(s.Id, "00123", null);
        Check("Identificador: exige poder responder", !last.Found && last.Message.Contains("iniciar sesión"), last.Message);

        // Versión 2: preguntas con IDs nuevos, una opción renombrada y otro orden
        var created = service.CreateVersion(new VersionActionRequest { SurveyId = s.Id, VersionId = v.Id });
        var v2 = created.Data.Versions.First(x => x.Number == 2);
        var q2 = v2.Questions.First(q => q.Text == "Interés");
        q2.Options.First(o => o.Text == "Bajo").Text = "Poco";
        v2.Questions.First(q => q.Text == "Comentarios").Order = 0;
        service.SaveDraft(new SaveDraftRequest { SurveyId = s.Id, Version = v2 });
        service.Publish(new VersionActionRequest { SurveyId = s.Id, VersionId = v2.Id });

        last = service.GetLastResponse(s.Id, "00123", "DOM\\otro");
        var com2 = v2.Questions.First(q => q.Text == "Comentarios").Id;
        Check("Identificador: captura de la versión anterior traducida a la nueva", last.Found && last.VersionNumber == 1
            && last.Answers[com2].Text == "segunda visita" && last.Answers[q2.Id].OptionIds.Single() == q2.Options.First(o => o.Text == "Alto").Id, last.Message);
        last = service.GetLastResponse(s.Id, "00999", "DOM\\otro");
        Check("Identificador: opción renombrada se omite, lo demás se carga", last.Found && !last.Answers.ContainsKey(q2.Id)
            && last.Answers[com2].Text == "otro cliente", last.Message);

        var dto = SurveyVersionCloner.Clone(v2, 3);
        Check("Identificador: se conserva al crear versión", dto.Questions.Count(q => q.Identifier) == 1);
    }

    // Citas -> archivo .ics para Outlook (Areas/Citas/Services/CalendarioIcs.cs)
    static void CitasIcsTests()
    {
        var inicio = new DateTime(2026, 11, 3, 10, 0, 0, DateTimeKind.Local);
        var notas = "Llevar estado de cuenta; revisar línea, plazo y tasa.\nSegunda línea con acentos: ñandú, acción, pingüino. " + new string('x', 120);
        var bytes = ModuloCitas.Services.CalendarioIcs.Crear(new ModuloCitas.Services.EventoCalendario
        {
            Uid = "cita-7@portal", Asunto = "Revisión, cartera; cliente", Inicio = inicio, Fin = inicio.AddMinutes(45),
            Lugar = "Sucursal Centro", Descripcion = notas, RecordatorioMinutos = 30
        });
        var ics = new System.Text.UTF8Encoding(false).GetString(bytes);
        var lines = ics.Split(new[] { "\r\n" }, StringSplitOptions.None);

        Check("ICS: sin BOM y con CRLF en todas las líneas", bytes[0] == (byte)'B' && !ics.Replace("\r\n", "").Contains("\n"));
        Check("ICS: ninguna línea pasa de 75 bytes", lines.All(l => System.Text.Encoding.UTF8.GetByteCount(l) <= 75),
            lines.Max(l => System.Text.Encoding.UTF8.GetByteCount(l)).ToString());
        var unfolded = ics.Replace("\r\n ", "");
        Check("ICS: inicio y fin en UTC", unfolded.Contains("DTSTART:" + inicio.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'"))
            && unfolded.Contains("DTEND:" + inicio.AddMinutes(45).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")));
        Check("ICS: asunto con , y ; escapados", unfolded.Contains("SUMMARY:Revisión\\, cartera\\; cliente"));
        Check("ICS: acentos y saltos de línea en notas intactos al desplegar",
            unfolded.Contains("DESCRIPTION:Llevar estado de cuenta\\; revisar línea\\, plazo y tasa.\\nSegunda línea con acentos: ñandú\\, acción\\, pingüino. " + new string('x', 120)));
        Check("ICS: recordatorio 30 min y UID estable", unfolded.Contains("TRIGGER:-PT30M") && unfolded.Contains("UID:cita-7@portal"));
        Check("ICS: estructura completa", unfolded.StartsWith("BEGIN:VCALENDAR\r\n") && unfolded.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n")
            && unfolded.Contains("METHOD:PUBLISH"));

        var sinAlarma = new System.Text.UTF8Encoding(false).GetString(ModuloCitas.Services.CalendarioIcs.Crear(new ModuloCitas.Services.EventoCalendario
        {
            Asunto = "Sin recordatorio", Inicio = inicio, Fin = inicio.AddHours(1)
        }));
        Check("ICS: sin recordatorio no agrega alarma ni lugar", !sinAlarma.Contains("VALARM") && !sinAlarma.Contains("LOCATION"));
        bool rechazada;
        try { ModuloCitas.Services.CalendarioIcs.Crear(new ModuloCitas.Services.EventoCalendario { Inicio = inicio, Fin = inicio }); rechazada = false; }
        catch (ArgumentException) { rechazada = true; }
        Check("ICS: fin igual al inicio rechazado", rechazada);
        Check("ICS: nombre de archivo seguro", ModuloCitas.Services.CalendarioIcs.NombreArchivo("cita 7/../x") == "cita-7----x.ics");
    }
}
