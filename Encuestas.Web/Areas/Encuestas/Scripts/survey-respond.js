/* =========================================================================
 * survey-respond.js
 * Página que ve el colaborador (Views/Encuestas/Responder.cshtml).
 *   1. Pregunta al servidor si el usuario puede responder (vigencia,
 *      autenticación, una respuesta por usuario; el segmento lo valida el portal).
 *   2. Si puede, carga la versión PUBLICADA y la muestra.
 *   3. Envía con SurveyApi.submit (el servidor vuelve a validar todo).
 *   Si la encuesta tiene pregunta identificador, "Buscar" precarga la última
 *   captura con ese valor (SurveyApi.lastResponse).
 * ========================================================================= */
(function ($) {
    'use strict';

    var esc = SurveyUtils.escapeHtml;

    /** Icono y título para cada motivo de SurveyService.GetEligibility. */
    var REASONS = {
        unavailable: { icon: 'bi-lock', title: 'Encuesta no disponible' },
        notStarted: { icon: 'bi-calendar', title: 'La encuesta aún no inicia' },
        ended: { icon: 'bi-calendar-x', title: 'La encuesta ya cerró' },
        authRequired: { icon: 'bi-person-lock', title: 'Inicia sesión para responder' },
        alreadyResponded: { icon: 'bi-check2-circle', title: 'Ya respondiste esta encuesta' },
        notInSegment: { icon: 'bi-people', title: 'Encuesta no disponible para tu perfil' }
    };

    function message(icon, title, text) {
        $('#surveyRoot').html(
            '<div class="sv-respond sv-thanks text-center">' +
            '<div class="sv-thanks-icon text-secondary"><i class="bi ' + icon + '"></i></div>' +
            '<h1 class="h4">' + esc(title) + '</h1><p class="text-muted mb-0">' + esc(text) + '</p></div>');
    }

    function loadFailed() {
        message('bi-exclamation-octagon', 'No fue posible cargar la encuesta', 'Intenta de nuevo más tarde.');
    }

    function mount(survey) {
        var version = $.grep(survey.versions, function (v) { return v.status === 'Published'; })[0];
        if (!version) {
            message(REASONS.unavailable.icon, REASONS.unavailable.title, 'Esta encuesta no está abierta en este momento.');
            return;
        }

        document.title = survey.name;
        SurveyRenderer.mountSurvey($('#surveyRoot'), {
            name: survey.name,
            version: version,
            onSubmit: function (answers) {
                var d = $.Deferred();
                SurveyApi.submit(survey.id, version.id, answers).done(function (result) {
                    if (result.ok) {
                        d.resolve();
                        return;
                    }
                    // El servidor revalida (obligatorias, lógica, límites, elegibilidad); muestra sus mensajes.
                    var detail = result.errors && result.errors.length ? ' ' + result.errors.slice(0, 3).join(' · ') : '';
                    SurveyUtils.toast(result.message + detail, 'danger');
                    d.reject();
                });
                return d.promise();
            },
            onLookup: function (value) {
                return SurveyApi.lastResponse(survey.id, value);
            }
        });
    }

    $(function () {
        var surveyId = SurveyApi.config.surveyId;
        SurveyApi.getEligibility(surveyId).done(function (eligibility) {
            if (!eligibility.canRespond) {
                var r = REASONS[eligibility.code] || REASONS.unavailable;
                message(r.icon, r.title, eligibility.reason);
                return;
            }
            SurveyApi.getPublished(surveyId).done(mount).fail(loadFailed);
        }).fail(loadFailed);
    });
})(jQuery);
