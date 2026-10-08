/* =========================================================================
 * survey-api.js
 * ÚNICA capa de acceso a datos del front-end. El resto de los módulos
 * nunca llama a $.ajax directamente.
 *
 * Dos implementaciones con la misma interfaz:
 *   - Mock   (SURVEY_CONFIG.useMock = true): datos de mock-data.js
 *            persistidos en localStorage para que la demo "recuerde".
 *   - Server (SURVEY_CONFIG.useMock = false): llama a SurveyApiController.
 *
 * Todas las funciones devuelven promesas jQuery.
 * Lecturas  -> se resuelven con los datos.
 * Escrituras-> se resuelven con { ok, message, errors, data }.
 * ========================================================================= */
var SurveyApi = (function ($) {
    'use strict';

    var U = SurveyUtils;
    var cfg = $.extend(true, {
        surveyId: 's1',
        useMock: true,
        urls: {
            index: '/Encuestas',
            constructor: '/Encuestas/Constructor/__ID__',
            respond: '/Encuestas/Responder/__ID__'
        },
        endpoints: {
            list: '/SurveyApi/List',
            create: '/SurveyApi/Create',
            published: '/SurveyApi/Published',
            history: '/SurveyApi/History',
            eligibility: '/SurveyApi/Eligibility',
            duplicate: '/SurveyApi/Duplicate',
            remove: '/SurveyApi/Delete',
            survey: '/SurveyApi/Survey',
            catalogs: '/SurveyApi/Catalogs',
            results: '/SurveyApi/Results',
            saveDraft: '/SurveyApi/SaveDraft',
            publish: '/SurveyApi/Publish',
            createVersion: '/SurveyApi/CreateVersion',
            changeStatus: '/SurveyApi/ChangeStatus',
            submit: '/SurveyApi/Submit'
        }
    }, window.SURVEY_CONFIG || {});

    // =====================================================================
    // Implementación SERVIDOR
    // =====================================================================
    var server = {
        list: function () {
            return $.getJSON(cfg.endpoints.list);
        },
        createSurvey: function (name, description) {
            return post(cfg.endpoints.create, { name: name, description: description });
        },
        getSurvey: function (surveyId) {
            return $.getJSON(cfg.endpoints.survey + '/' + encodeURIComponent(surveyId));
        },
        getPublished: function (surveyId) {
            return $.getJSON(cfg.endpoints.published + '/' + encodeURIComponent(surveyId));
        },
        getHistory: function (surveyId) {
            return $.getJSON(cfg.endpoints.history + '/' + encodeURIComponent(surveyId));
        },
        getEligibility: function (surveyId) {
            return $.getJSON(cfg.endpoints.eligibility + '/' + encodeURIComponent(surveyId));
        },
        duplicateSurvey: function (surveyId) {
            return post(cfg.endpoints.duplicate, { surveyId: surveyId });
        },
        deleteSurvey: function (surveyId) {
            return post(cfg.endpoints.remove, { surveyId: surveyId });
        },
        getCatalogs: function () {
            return $.getJSON(cfg.endpoints.catalogs);
        },
        getResults: function (surveyId, versionId) {
            return $.getJSON(cfg.endpoints.results + '/' + encodeURIComponent(versionId), { surveyId: surveyId });
        },
        saveDraft: function (surveyId, name, version) {
            return post(cfg.endpoints.saveDraft, { surveyId: surveyId, name: name, version: version });
        },
        publish: function (surveyId, versionId) {
            return post(cfg.endpoints.publish, { surveyId: surveyId, versionId: versionId });
        },
        createVersion: function (surveyId, fromVersionId) {
            return post(cfg.endpoints.createVersion, { surveyId: surveyId, versionId: fromVersionId });
        },
        changeStatus: function (surveyId, versionId, status) {
            return post(cfg.endpoints.changeStatus, { surveyId: surveyId, versionId: versionId, status: status });
        },
        submit: function (surveyId, versionId, answers) {
            return post(cfg.endpoints.submit, { surveyId: surveyId, versionId: versionId, answers: answers });
        }
    };

    /** Token antifalsificación que genera @Html.AntiForgeryToken() en _SurveyConfig.cshtml. */
    function antiForgeryToken() {
        return $('input[name="__RequestVerificationToken"]').first().val() || '';
    }

    /**
     * POST JSON con el token en el encabezado RequestVerificationToken.
     * Los errores 400/401/403 del servidor vienen como ApiResult y se resuelven igual.
     */
    function post(url, body) {
        var d = $.Deferred();
        $.ajax({
            url: url,
            type: 'POST',
            contentType: 'application/json; charset=utf-8',
            dataType: 'json',
            headers: { RequestVerificationToken: antiForgeryToken() },
            data: JSON.stringify(body)
        })
            .done(function (r) { d.resolve(r); })
            .fail(function (xhr) {
                if (xhr.responseJSON && xhr.responseJSON.ok === false) d.resolve(xhr.responseJSON);
                else d.resolve({ ok: false, message: 'No fue posible comunicarse con el servidor (' + xhr.status + ').', errors: [] });
            });
        return d.promise();
    }

    // =====================================================================
    // Implementación MOCK (replica las reglas de SurveyService.cs)
    // =====================================================================
    var STORAGE_KEY = 'sv-demo-db-v1';
    var db = null;

    function loadDb() {
        if (db) return db;
        try {
            var raw = window.localStorage.getItem(STORAGE_KEY);
            if (raw) db = JSON.parse(raw);
        } catch (e) { db = null; }

        if (!db) {
            var survey = SurveyMock.buildSurvey();
            db = { surveys: {}, responses: {} };
            db.surveys[survey.id] = survey;
            $.each(survey.versions, function (_, v) {
                db.responses[v.id] = v.responseCount ? SurveyMock.generateResponses(v, v.responseCount, 7) : [];
            });
            persist();
        }
        return db;
    }

    function persist() {
        try { window.localStorage.setItem(STORAGE_KEY, JSON.stringify(db)); } catch (e) { /* almacenamiento no disponible: la demo sigue en memoria */ }
    }

    /** Simula latencia de red y devuelve copias (como haría el servidor). */
    function later(value) {
        var d = $.Deferred();
        setTimeout(function () { d.resolve(U.clone(value)); }, 180);
        return d.promise();
    }

    function ok(data, message) { return later({ ok: true, message: message, errors: [], data: data }); }
    function fail(message, errors) { return later({ ok: false, message: message, errors: errors || [] }); }

    function editable(v) {
        return v.status === 'Draft' || (v.status === 'Published' && !v.responseCount);
    }

    function find(surveyId, versionId) {
        var survey = loadDb().surveys[surveyId];
        var version = null;
        if (survey) $.each(survey.versions, function (_, v) { if (v.id === versionId) version = v; });
        return { survey: survey, version: version };
    }

    function nextNumber(survey) {
        var max = 0;
        $.each(survey.versions, function (_, v) { max = Math.max(max, v.number); });
        return max + 1;
    }

    function draftOf(survey) {
        var d = null;
        $.each(survey.versions, function (_, v) { if (v.status === 'Draft') d = v; });
        return d;
    }

    /** Estimación de audiencia: OR dentro de cada dimensión, AND entre dimensiones (igual que SurveyService.EstimateAudience). */
    function audience(seg, catalogs) {
        var c = catalogs || (window.SurveyMock ? SurveyMock.catalogs : { branches: [] });
        seg = seg || {};
        var all = seg.mode !== 'segmented';
        var has = function (list, value) { return !list || !list.length || $.inArray(value, list) >= 0; };
        var total = 0;
        $.each(c.branches, function (_, b) {
            if (!all && !(has(seg.regions, b.regionId) && has(seg.segments, b.segmentId) && has(seg.branches, b.id))) return;
            $.each(b.headcount, function (role, n) {
                if (all || has(seg.roles, role)) total += n;
            });
        });
        return total + (all ? 0 : (seg.users || []).length);
    }

    /** Fila del listado (misma regla que SurveyService.GetSurveyList). */
    function summarize(s) {
        var byStatus = function (st) { return $.grep(s.versions, function (v) { return v.status === st; })[0]; };
        var current = byStatus('Published') || byStatus('Draft') || s.versions.slice().sort(function (a, b) { return b.number - a.number; })[0];
        var draft = byStatus('Draft');
        var total = 0;
        $.each(s.versions, function (_, v) { total += v.responseCount || 0; });
        return {
            id: s.id,
            name: s.name,
            description: s.description,
            status: current ? current.status : 'Draft',
            currentVersion: current ? current.number : 0,
            draftVersion: draft && draft !== current ? draft.number : null,
            responseCount: current ? current.responseCount || 0 : 0,
            totalResponses: total,
            versionCount: s.versions.length,
            startDate: current ? current.settings.startDate : null,
            endDate: current ? current.settings.endDate : null
        };
    }

    /** Bitácora de la demo (misma forma que AuditEntryDto y mismos textos que SurveyService). */
    function audit(surveyId, versionNumber, action, detail) {
        (db.audit = db.audit || []).push({
            surveyId: surveyId, versionNumber: versionNumber, action: action, detail: detail,
            userName: 'Tú (demo)', createdAt: new Date().toISOString().substring(0, 19)
        });
    }

    var mock = {
        getHistory: function (surveyId) {
            var list = $.grep(loadDb().audit || [], function (a) { return a.surveyId === surveyId; });
            return later(list.slice().reverse().slice(0, 100));
        },
        list: function () {
            var rows = [];
            $.each(loadDb().surveys, function (_, s) { rows.push(summarize(s)); });
            return later(rows);
        },
        createSurvey: function (name, description) {
            name = $.trim(name || '');
            if (!name) return fail('Escribe el nombre de la encuesta.');
            var n = 1;
            while (loadDb().surveys['s' + n]) n += 1;
            var today = U.todayIso();
            var end = new Date(); end.setDate(end.getDate() + 30);
            var survey = {
                id: 's' + n,
                name: name,
                description: description || '',
                versions: [{
                    id: U.newId('v'), number: 1, status: 'Draft', responseCount: 0,
                    createdAt: new Date().toISOString().substring(0, 19), publishedAt: null,
                    settings: { startDate: today, endDate: end.toISOString().substring(0, 10), allowMultipleResponses: false, anonymous: false, requireAuth: true, welcomeMessage: '', thankYouMessage: '¡Gracias por tu respuesta!' },
                    segmentation: { mode: 'all', roles: [], segments: [], regions: [], branches: [], users: [] },
                    questions: []
                }]
            };
            db.surveys[survey.id] = survey;
            db.responses[survey.versions[0].id] = [];
            audit(survey.id, 1, 'created', 'Creó la encuesta «' + survey.name + '».');
            persist();
            return ok(survey, 'Encuesta creada.');
        },
        getSurvey: function (surveyId) {
            var s = loadDb().surveys[surveyId];
            return s ? later(s) : $.Deferred().reject({ status: 404 }).promise();
        },
        /** Misma regla que SurveyService.DuplicateSurvey: copia el borrador (o la publicada) como encuesta nueva. */
        duplicateSurvey: function (surveyId) {
            var source = loadDb().surveys[surveyId];
            if (!source) return fail('La encuesta no existe.');
            var byStatus = function (st) { return $.grep(source.versions, function (v) { return v.status === st; })[0]; };
            var from = byStatus('Draft') || byStatus('Published') || source.versions[source.versions.length - 1];
            var n = 1;
            while (db.surveys['s' + n]) n += 1;
            var copy = { id: 's' + n, name: 'Copia de ' + source.name, description: source.description, versions: [U.cloneVersion(from, 1)] };
            db.surveys[copy.id] = copy;
            db.responses[copy.versions[0].id] = [];
            audit(copy.id, 1, 'duplicated', 'Creó la encuesta como copia de «' + source.name + '» (versión ' + from.number + ').');
            persist();
            return ok(copy, 'Encuesta duplicada como borrador.');
        },
        /** Solo sin respuestas (igual que SurveyService.DeleteSurvey). */
        deleteSurvey: function (surveyId) {
            var s = loadDb().surveys[surveyId];
            if (!s) return fail('La encuesta no existe.');
            if ($.grep(s.versions, function (v) { return v.responseCount > 0; }).length) {
                return fail('La encuesta tiene respuestas y no puede eliminarse. Ciérrala o archívala para conservar el histórico.');
            }
            delete db.surveys[surveyId];
            audit(surveyId, null, 'deleted', 'Eliminó la encuesta «' + s.name + '».');
            persist();
            return ok(surveyId, 'Encuesta eliminada.');
        },
        /**
         * Misma regla que SurveyService.GetEligibility. En la demo no hay usuarios:
         * "ya respondiste" se recuerda por navegador.
         */
        getEligibility: function (surveyId) {
            var s = loadDb().surveys[surveyId];
            var v = s ? $.grep(s.versions, function (x) { return x.status === 'Published'; })[0] : null;
            var denied = function (code, reason) { return later({ canRespond: false, code: code, reason: reason }); };
            if (!v) return denied('unavailable', 'Esta encuesta no está abierta en este momento.');
            var today = U.todayIso();
            if (v.settings.startDate && today < v.settings.startDate) return denied('notStarted', 'La encuesta estará disponible a partir del ' + U.formatDate(v.settings.startDate) + '.');
            if (v.settings.endDate && today > v.settings.endDate) return denied('ended', 'El periodo para responder terminó el ' + U.formatDate(v.settings.endDate) + '.');
            if (!v.settings.allowMultipleResponses && $.inArray(v.id, db.respondedHere || []) >= 0) {
                return denied('alreadyResponded', 'Tu respuesta ya quedó registrada. ¡Gracias por participar!');
            }
            return later({ canRespond: true, versionId: v.id });
        },
        /** Solo la versión publicada y sin segmentación (igual que SurveyService.GetPublished). */
        getPublished: function (surveyId) {
            var s = loadDb().surveys[surveyId];
            if (!s) return $.Deferred().reject({ status: 404 }).promise();
            var published = $.grep(s.versions, function (v) { return v.status === 'Published'; })[0];
            return later({
                id: s.id,
                name: s.name,
                versions: published ? [{ id: published.id, number: published.number, status: published.status, settings: published.settings, questions: published.questions }] : []
            });
        },
        getCatalogs: function () {
            return later(SurveyMock.catalogs);
        },
        getResults: function (surveyId, versionId) {
            var f = find(surveyId, versionId);
            if (!f.version) return $.Deferred().reject({ status: 404 }).promise();
            return later({
                versionId: versionId,
                audience: audience(f.version.segmentation),
                startDate: f.version.settings.startDate,
                endDate: f.version.settings.endDate,
                responses: db.responses[versionId] || []
            });
        },
        saveDraft: function (surveyId, name, version) {
            var f = find(surveyId, version.id);
            if (!f.version) return fail('La versión no existe.');
            if (name) f.survey.name = name;

            if (!editable(f.version)) {
                // Regla de versionado: no sobrescribir una versión con respuestas.
                if (draftOf(f.survey)) return fail('La versión ' + f.version.number + ' tiene respuestas. Ya existe un borrador; edítalo en su lugar.');
                var nv = U.cloneVersion(version, nextNumber(f.survey));
                f.survey.versions.push(nv);
                db.responses[nv.id] = [];
                audit(surveyId, nv.number, 'newVersion', 'Guardó cambios sobre la versión ' + f.version.number + ' (con respuestas); se creó la versión ' + nv.number + '.');
                persist();
                return ok(f.survey, 'La versión ' + f.version.number + ' tiene respuestas; se creó la versión ' + nv.number + ' (borrador).');
            }

            f.version.settings = U.clone(version.settings);
            f.version.segmentation = U.clone(version.segmentation);
            f.version.questions = U.clone(version.questions);
            audit(surveyId, f.version.number, 'saved', 'Guardó la versión ' + f.version.number + ' (' + f.version.questions.length + ' preguntas).');
            persist();
            return ok(f.survey, 'Borrador guardado.');
        },
        publish: function (surveyId, versionId) {
            var f = find(surveyId, versionId);
            if (!f.version) return fail('La versión no existe.');
            if (f.version.status !== 'Draft') return fail('Solo se puede publicar una versión en borrador.');
            var errors = validateForPublish(f.version);
            if (errors.length) return fail('La encuesta tiene errores.', errors);

            $.each(f.survey.versions, function (_, v) {
                if (v.status !== 'Published') return;
                v.status = 'Archived';
                audit(surveyId, v.number, 'archived', 'La versión ' + v.number + ' pasó al histórico al publicar la versión ' + f.version.number + '.');
            });
            f.version.status = 'Published';
            f.version.publishedAt = new Date().toISOString().substring(0, 19);
            audit(surveyId, f.version.number, 'published', 'Publicó la versión ' + f.version.number + ' (vigencia ' +
                (f.version.settings.startDate || '?') + ' a ' + (f.version.settings.endDate || '?') + ').');
            persist();
            return ok(f.survey, 'Versión ' + f.version.number + ' publicada.');
        },
        createVersion: function (surveyId, fromVersionId) {
            var f = find(surveyId, fromVersionId);
            if (!f.survey) return fail('La encuesta no existe.');
            var existing = draftOf(f.survey);
            if (existing) return ok(f.survey, 'Ya existe el borrador v' + existing.number + '.');
            if (!f.version) return fail('La versión origen no existe.');
            var nv = U.cloneVersion(f.version, nextNumber(f.survey));
            f.survey.versions.push(nv);
            db.responses[nv.id] = [];
            audit(surveyId, nv.number, 'newVersion', 'Creó la versión ' + nv.number + ' a partir de la versión ' + f.version.number + '.');
            persist();
            return ok(f.survey, 'Se creó la versión ' + nv.number + ' (borrador).');
        },
        changeStatus: function (surveyId, versionId, status) {
            var f = find(surveyId, versionId);
            if (!f.version) return fail('La versión no existe.');
            if (status === 'Closed' && f.version.status !== 'Published') return fail('Solo se puede cerrar una versión publicada.');
            f.version.status = status;
            audit(surveyId, f.version.number, status === 'Closed' ? 'closed' : 'archived',
                (status === 'Closed' ? 'Cerró' : 'Archivó') + ' la versión ' + f.version.number + ' (' + (f.version.responseCount || 0) + ' respuestas).');
            persist();
            return ok(f.survey, 'Estado actualizado.');
        },
        submit: function (surveyId, versionId, answers) {
            var f = find(surveyId, versionId);
            if (!f.version || f.version.status !== 'Published') return fail('La encuesta no está disponible.');
            db.respondedHere = db.respondedHere || [];
            if (!f.version.settings.allowMultipleResponses && $.inArray(versionId, db.respondedHere) >= 0) return fail('Ya respondiste esta encuesta.');
            db.respondedHere.push(versionId);
            var record = { id: U.newId('r'), submittedAt: new Date().toISOString().substring(0, 19), answers: answers };
            (db.responses[versionId] = db.responses[versionId] || []).push(record);
            f.version.responseCount = (f.version.responseCount || 0) + 1;
            persist();
            return ok(record.id, f.version.settings.thankYouMessage);
        },
        /** Solo demo: borra lo guardado en localStorage y regenera los datos. */
        reset: function () {
            try { window.localStorage.removeItem(STORAGE_KEY); } catch (e) { /* sin acceso */ }
            db = null;
            loadDb();
        }
    };

    // =====================================================================
    // Validación previa a publicar (misma regla que SurveyService.ValidateForPublish)
    // =====================================================================
    function validateForPublish(version, catalogs) {
        var errors = [];
        var questions = SurveyLogic.sorted(version.questions || []);
        if (!questions.length) errors.push('La encuesta no tiene preguntas.');

        $.each(questions, function (i, q) {
            var label = 'P' + (i + 1);
            var t = SurveyTypes.get(q.type);
            var v = q.validation || {};
            if (!$.trim(q.text)) errors.push(label + ': falta el texto de la pregunta.');
            if (t.options && $.grep(q.options, function (o) { return $.trim(o.text); }).length < 2) errors.push(label + ': requiere al menos 2 opciones.');
            if (q.type === 'matrix' && (!q.matrix.rows.length || q.matrix.columns.length < 2)) errors.push(label + ': la matriz requiere filas y al menos 2 columnas.');
            if (q.type === 'dependent' && !$.grep(q.dependent.options, function (o) { return o.level === 0; }).length) errors.push(label + ': la lista dependiente no tiene opciones.');
            if (v.minLength && v.maxLength && v.minLength > v.maxLength) errors.push(label + ': el mínimo de caracteres es mayor que el máximo.');
            if (v.minSelections && v.maxSelections && v.minSelections > v.maxSelections) errors.push(label + ': el mínimo de selecciones es mayor que el máximo.');
            if (v.minValue !== null && v.minValue !== undefined && v.maxValue !== null && v.maxValue !== undefined && v.minValue > v.maxValue) errors.push(label + ': el valor mínimo es mayor que el máximo.');
            if (q.type === 'scale' && Number(v.scaleMin) >= Number(v.scaleMax)) errors.push(label + ': la escala debe tener un mínimo menor que el máximo.');
        });

        errors = errors.concat(SurveyLogic.validate(questions));

        var s = version.settings || {};
        if (s.startDate && s.endDate && s.endDate < s.startDate) errors.push('Configuración: la fecha de cierre es anterior a la fecha de inicio.');
        if (version.segmentation && version.segmentation.mode === 'segmented' && audience(version.segmentation, catalogs) === 0) {
            errors.push('Segmentación: la audiencia seleccionada no tiene colaboradores.');
        }
        return errors;
    }

    var impl = cfg.useMock ? mock : server;

    /** URL de una página del módulo: url('constructor', '12') -> /Encuestas/Constructor/12 */
    function url(kind, surveyId) {
        return (cfg.urls[kind] || '').replace('__ID__', encodeURIComponent(surveyId || ''));
    }

    return {
        config: cfg,
        isMock: !!cfg.useMock,
        url: url,
        list: impl.list,
        createSurvey: impl.createSurvey,
        getSurvey: impl.getSurvey,
        getPublished: impl.getPublished,
        getEligibility: impl.getEligibility,
        getHistory: impl.getHistory,
        duplicateSurvey: impl.duplicateSurvey,
        deleteSurvey: impl.deleteSurvey,
        getCatalogs: impl.getCatalogs,
        getResults: impl.getResults,
        saveDraft: impl.saveDraft,
        publish: impl.publish,
        createVersion: impl.createVersion,
        changeStatus: impl.changeStatus,
        submit: impl.submit,
        resetDemo: mock.reset,
        validateForPublish: validateForPublish,
        estimateAudience: audience
    };
})(jQuery);
