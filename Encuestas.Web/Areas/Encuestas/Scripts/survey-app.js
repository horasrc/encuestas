/* =========================================================================
 * survey-app.js
 * Punto de entrada del constructor. Inicializa los módulos, carga datos
 * y conecta las acciones del encabezado (guardar, publicar, versiones...).
 *
 *   SurveyApi  (datos)  ->  SurveyStore (estado)  ->  módulos de UI
 * ========================================================================= */
(function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var Api = SurveyApi;
    var Store = SurveyStore;

    // ---------------------------------------------------------------------
    // Carga
    // ---------------------------------------------------------------------

    function loadSurvey(preferredVersionId) {
        return Api.getSurvey(Api.config.surveyId)
            .done(function (survey) {
                Store.setSurvey(survey, preferredVersionId);
                $('#surveyPage').attr('aria-busy', 'false');
            })
            .fail(function () {
                $('#tabBuilder').html('<div class="alert alert-danger"><i class="bi bi-exclamation-octagon me-2"></i>No fue posible cargar la encuesta.</div>');
            });
    }

    /** Procesa el resultado de una escritura y recarga el estado. */
    function applyResult(result, pickVersionId) {
        if (!result.ok) {
            U.toast(result.message || 'No se pudo completar la operación.', 'danger');
            return false;
        }
        Store.setSurvey(result.data, pickVersionId);
        if (result.message) U.toast(result.message, 'success');
        return true;
    }

    function busy($btn, on) {
        $btn.prop('disabled', on);
        if (on) $btn.data('html', $btn.html()).html('<span class="spinner-border spinner-border-sm me-1" aria-hidden="true"></span> Procesando…');
        else if ($btn.data('html')) $btn.html($btn.data('html'));
    }

    // ---------------------------------------------------------------------
    // Guardar borrador
    // ---------------------------------------------------------------------

    function save() {
        var survey = Store.survey();
        var version = Store.version();
        if (!Store.isEditable()) return $.Deferred().reject().promise();

        var $btn = $('#btnSaveDraft');
        busy($btn, true);
        return Api.saveDraft(survey.id, survey.name, version).then(function (result) {
            busy($btn, false);
            // Si el servidor creó una versión nueva (regla de versionado), se abre el borrador.
            var keep = $.grep(result.data ? result.data.versions : [], function (v) { return v.id === version.id && (v.status === 'Draft' || !v.responseCount); }).length;
            var draft = result.data ? $.grep(result.data.versions, function (v) { return v.status === 'Draft'; })[0] : null;
            var ok = applyResult(result, keep ? version.id : (draft ? draft.id : null));
            return ok ? $.Deferred().resolve().promise() : $.Deferred().reject().promise();
        }, function () { busy($btn, false); });
    }

    // ---------------------------------------------------------------------
    // Publicar
    // ---------------------------------------------------------------------

    function openPublish() {
        var version = Store.version();
        if (!version || version.status !== 'Draft') return;
        var errors = Api.validateForPublish(version, Store.state.catalogs);
        var s = version.settings || {};
        var sg = version.segmentation || {};
        var published = $.grep(Store.versions(), function (v) { return v.status === 'Published'; })[0];
        var rules = 0, required = 0;
        $.each(version.questions, function (_, q) { rules += (q.rules || []).length; if (q.required) required += 1; });

        $('#publishErrors').toggleClass('d-none', !errors.length).html(errors.length
            ? '<div class="fw-semibold mb-1"><i class="bi bi-exclamation-octagon me-1"></i>Corrige lo siguiente antes de publicar:</div><ul class="mb-0">' +
              $.map(errors, function (e) { return '<li>' + esc(e) + '</li>'; }).join('') + '</ul>'
            : '');

        $('#publishSummary').html(
            '<div class="row g-3">' +
            summaryItem('bi-ui-checks', 'Preguntas', version.questions.length + ' (' + required + ' obligatorias)') +
            summaryItem('bi-signpost-split', 'Reglas condicionales', rules) +
            summaryItem('bi-calendar-range', 'Vigencia', U.formatDate(s.startDate) + ' – ' + U.formatDate(s.endDate)) +
            summaryItem('bi-people', 'Audiencia estimada', Api.estimateAudience(sg, Store.state.catalogs).toLocaleString('es-MX') + ' colaboradores') +
            summaryItem('bi-person-check', 'Respuestas por usuario', s.allowMultipleResponses ? 'Múltiples' : 'Una') +
            summaryItem('bi-incognito', 'Anónima', s.anonymous ? 'Sí' : 'No') +
            '</div>' +
            (published ? '<div class="alert alert-light border small mt-3 mb-0"><i class="bi bi-clock-history me-1"></i>La versión ' + published.number +
                ' (' + U.count(published.responseCount, 'respuesta') + ') pasará a <strong>Archivada</strong> y conservará sus resultados.</div>' : '') +
            (Store.state.dirty ? '<div class="small text-muted mt-2"><i class="bi bi-floppy me-1"></i>Los cambios sin guardar se guardarán antes de publicar.</div>' : '')
        );
        $('#btnConfirmPublish').prop('disabled', errors.length > 0);
        bootstrap.Modal.getOrCreateInstance($('#publishModal')[0]).show();
    }

    function summaryItem(icon, label, value) {
        return '<div class="col-sm-6"><div class="sv-summary-item"><i class="bi ' + icon + '"></i><div>' +
            '<div class="small text-muted">' + esc(label) + '</div><div class="fw-semibold">' + esc(value) + '</div></div></div></div>';
    }

    function publish() {
        var $btn = $('#btnConfirmPublish');
        var versionId = Store.version().id;
        busy($btn, true);
        var saved = Store.state.dirty ? save() : $.Deferred().resolve().promise();
        saved.done(function () {
            Api.publish(Store.survey().id, versionId).done(function (result) {
                busy($btn, false);
                if (!result.ok && result.errors && result.errors.length) {
                    $('#publishErrors').removeClass('d-none').html('<ul class="mb-0">' + $.map(result.errors, function (e) { return '<li>' + esc(e) + '</li>'; }).join('') + '</ul>');
                    return;
                }
                if (applyResult(result, versionId)) bootstrap.Modal.getOrCreateInstance($('#publishModal')[0]).hide();
            });
        }).fail(function () { busy($btn, false); });
    }

    // ---------------------------------------------------------------------
    // Versiones
    // ---------------------------------------------------------------------

    function confirmDiscard() {
        if (!Store.state.dirty) return $.Deferred().resolve().promise();
        return U.confirmDialog({
            title: 'Cambios sin guardar',
            message: '<p>Tienes cambios sin guardar en esta versión. Si continúas se descartarán.</p>',
            confirmText: 'Descartar y continuar',
            danger: true
        });
    }

    function openVersion(versionId) {
        confirmDiscard().done(function () {
            bootstrap.Offcanvas.getOrCreateInstance($('#versionsOffcanvas')[0]).hide();
            loadSurvey(versionId); // recarga desde la API para descartar cambios en memoria
        });
    }

    function createVersion(fromVersionId) {
        var draft = Store.draftVersion();
        if (draft) { openVersion(draft.id); return; }
        confirmDiscard().done(function () {
            Api.createVersion(Store.survey().id, fromVersionId).done(function (result) {
                var newDraft = result.data ? $.grep(result.data.versions, function (v) { return v.status === 'Draft'; })[0] : null;
                if (applyResult(result, newDraft ? newDraft.id : null)) {
                    bootstrap.Offcanvas.getOrCreateInstance($('#versionsOffcanvas')[0]).hide();
                }
            });
        });
    }

    function changeStatus(status) {
        var v = Store.version();
        var texts = {
            Closed: { title: 'Cerrar encuesta', msg: 'La versión ' + v.number + ' dejará de recibir respuestas. Los resultados se conservan.', btn: 'Cerrar encuesta' },
            Archived: { title: 'Archivar versión', msg: 'La versión ' + v.number + ' pasará al histórico. Los resultados se conservan.', btn: 'Archivar' }
        }[status];
        U.confirmDialog({ title: texts.title, message: '<p>' + esc(texts.msg) + '</p>', confirmText: texts.btn, danger: true }).done(function () {
            Api.changeStatus(Store.survey().id, v.id, status).done(function (result) { applyResult(result, v.id); });
        });
    }

    // ---------------------------------------------------------------------
    // Arranque
    // ---------------------------------------------------------------------

    function bindHeader() {
        $('#btnSaveDraft').on('click', save);
        $('#btnPublish').on('click', openPublish);
        $('#btnConfirmPublish').on('click', publish);
        $('#btnCloseSurvey').on('click', function () { changeStatus('Closed'); });
        $('#btnArchive').on('click', function () { changeStatus('Archived'); });

        $('#btnNewVersion').on('click', function () {
            var draftId = $(this).attr('data-draft-id');
            if (draftId) openVersion(draftId); else createVersion(Store.version().id);
        });

        $('#versionsList').on('click', '[data-open-version]', function () { openVersion($(this).attr('data-open-version')); });
        $('#versionsList').on('click', '[data-copy-version]', function () { createVersion($(this).attr('data-copy-version')); });

        $('#btnCopyLink').on('click', function () {
            window.open(Api.url('respond', Store.survey().id), '_blank', 'noopener');
        });

        $('#btnResetDemo').on('click', function () {
            U.confirmDialog({ title: 'Restablecer demo', message: '<p>Se borrarán los cambios guardados en este navegador y se cargarán los datos de ejemplo.</p>', confirmText: 'Restablecer', danger: true })
                .done(function () { Api.resetDemo(); loadSurvey(); U.toast('Datos de ejemplo restablecidos.', 'info'); });
        });

        // Ctrl/Cmd + S guarda el borrador.
        $(document).on('keydown', function (e) {
            if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S')) {
                e.preventDefault();
                if (Store.isEditable()) save();
            }
        });

        // Aviso al salir con cambios sin guardar.
        $(window).on('beforeunload', function (e) {
            if (Store.state.dirty) {
                e.preventDefault();
                return '';
            }
        });

        Store.on('survey:loaded', function () {
            SurveyBuilder.renderHeader();
            if ($('#tabResults').hasClass('active')) SurveyResults.load();
            if ($('#tabPreview').hasClass('active')) SurveyPreview.render();
        });
    }

    $(function () {
        $('.sv-back').attr('href', Api.url('index'));

        // /Encuestas/Constructor/12#resultados abre directamente la pestaña Resultados.
        var openResults = window.location.hash === '#resultados';
        Store.on('survey:loaded', function () {
            if (!openResults) return;
            openResults = false; // solo en la primera carga
            bootstrap.Tab.getOrCreateInstance(document.querySelector('#tabResultsBtn')).show();
        });

        SurveyBuilder.init();
        SurveyEditor.init();
        SurveySettings.init();
        SurveyPreview.init();
        SurveyResults.init();
        bindHeader();

        Api.getCatalogs().done(function (catalogs) {
            Store.state.catalogs = catalogs;
            loadSurvey();
        }).fail(function () {
            U.toast('No se pudieron cargar los catálogos de segmentación.', 'danger');
            loadSurvey();
        });
    });
})(jQuery);
