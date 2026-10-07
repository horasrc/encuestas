using System;
using System.Collections.Generic;
using System.Linq;
using Encuestas.Web.Models.Dto;
using Encuestas.Web.Services;

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

        Console.WriteLine(failures == 0 ? "TODAS LAS PRUEBAS PASARON" : failures + " FALLAS");
    }
}
