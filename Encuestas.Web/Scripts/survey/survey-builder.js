/* =========================================================================
 * survey-builder.js
 * Pestaña "Constructor" (salvo el panel de edición, que está en
 * survey-editor.js) y el encabezado:
 *   - Lista de preguntas (seleccionar, ordenar, duplicar, eliminar)
 *   - Menú "Agregar pregunta" y paleta "Tipos de pregunta"
 *   - Vista previa de la pregunta seleccionada
 *   - Resumen de la lógica condicional
 *   - Encabezado: nombre, estado, versión, aviso de solo lectura
 *   - Historial de versiones (offcanvas)
 * ========================================================================= */
var SurveyBuilder = (function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var Store = SurveyStore;

    var statusInfo = U.statusInfo;

    // ---------------------------------------------------------------------
    // Lista de preguntas
    // ---------------------------------------------------------------------

    function renderList() {
        var questions = Store.questions();
        var editable = Store.isEditable();
        var $list = $('#questionList').empty();
        $('#questionCount').text(questions.length);

        if (!questions.length) {
            $list.append('<div class="sv-empty"><i class="bi bi-ui-checks"></i><p>Aún no hay preguntas.<br>Agrega la primera con el botón de abajo.</p></div>');
            return;
        }

        $.each(questions, function (i, q) {
            var type = SurveyTypes.get(q.type);
            var incoming = SurveyLogic.incomingRules(q.id, questions);
            var badges = '';
            if (q.required) badges += '<span class="badge sv-badge-required">Obligatoria</span>';
            if ((q.rules || []).length) badges += '<span class="badge sv-badge-logic" title="Tiene reglas condicionales"><i class="bi bi-signpost-split"></i> ' + q.rules.length + '</span>';
            if (incoming.length) {
                var src = [];
                $.each(incoming, function (_, x) {
                    var label = SurveyLogic.questionLabel(questions, x.source.id);
                    if ($.inArray(label, src) < 0) src.push(label);
                });
                badges += '<span class="badge sv-badge-cond" title="Depende de otras preguntas"><i class="bi bi-eye"></i> Depende de ' + esc(src.join(', ')) + '</span>';
            }

            var $item = $(
                '<div class="list-group-item sv-q-item' + (q.id === Store.state.selectedId ? ' active' : '') + '" data-qid="' + esc(q.id) + '" data-sort-id="' + esc(q.id) + '" draggable="false" tabindex="0" role="button" aria-label="Pregunta ' + (i + 1) + '">' +
                (editable ? '<span class="drag-handle" title="Arrastrar para reordenar"><i class="bi bi-grip-vertical"></i></span>' : '') +
                '<span class="sv-q-num">' + (i + 1) + '</span>' +
                '<div class="sv-q-body">' +
                '  <div class="sv-q-type"><i class="bi ' + type.icon + '"></i> ' + esc(type.label) + '</div>' +
                '  <div class="sv-q-text">' + (esc(q.text) || '<em class="text-muted">Sin texto</em>') + '</div>' +
                (badges ? '<div class="sv-q-badges">' + badges + '</div>' : '') +
                '</div>' +
                (editable ?
                    '<div class="dropdown sv-q-menu">' +
                    '  <button class="btn btn-sm btn-link text-muted" type="button" data-bs-toggle="dropdown" aria-expanded="false" aria-label="Acciones"><i class="bi bi-three-dots-vertical"></i></button>' +
                    '  <ul class="dropdown-menu dropdown-menu-end shadow-sm">' +
                    '    <li><button class="dropdown-item" type="button" data-act="duplicate"><i class="bi bi-copy me-2"></i>Duplicar</button></li>' +
                    '    <li><button class="dropdown-item" type="button" data-act="up"' + (i === 0 ? ' disabled' : '') + '><i class="bi bi-arrow-up me-2"></i>Subir</button></li>' +
                    '    <li><button class="dropdown-item" type="button" data-act="down"' + (i === questions.length - 1 ? ' disabled' : '') + '><i class="bi bi-arrow-down me-2"></i>Bajar</button></li>' +
                    '    <li><hr class="dropdown-divider"></li>' +
                    '    <li><button class="dropdown-item text-danger" type="button" data-act="delete"><i class="bi bi-trash me-2"></i>Eliminar</button></li>' +
                    '  </ul>' +
                    '</div>' : '') +
                '</div>');
            $list.append($item);
        });
    }

    /** Acciones comunes de pregunta (las usan la lista y el editor). */
    function questionAction(act, id) {
        if (!Store.isEditable()) return;
        if (act === 'duplicate') Store.duplicate(id);
        else if (act === 'up') Store.move(id, -1);
        else if (act === 'down') Store.move(id, 1);
        else if (act === 'delete') confirmDelete(id);
    }

    function confirmDelete(id) {
        var questions = Store.questions();
        var q = Store.question(id);
        var incoming = SurveyLogic.incomingRules(id, questions);
        var msg = '<p>¿Eliminar <strong>' + esc(SurveyLogic.questionLabel(questions, id)) + '</strong>' +
            (q.text ? ' «' + esc(q.text) + '»' : '') + '?</p>' +
            (incoming.length ? '<div class="alert alert-warning small mb-0"><i class="bi bi-exclamation-triangle me-1"></i>También se eliminarán ' +
                incoming.length + ' regla(s) condicional(es) que apuntan a esta pregunta.</div>' : '');
        U.confirmDialog({ title: 'Eliminar pregunta', message: msg, confirmText: 'Eliminar', danger: true })
            .done(function () { Store.remove(id); U.toast('Pregunta eliminada.', 'info'); });
    }

    // ---------------------------------------------------------------------
    // Agregar pregunta (menú + paleta)
    // ---------------------------------------------------------------------

    function renderTypeMenus() {
        var menu = '';
        var palette = '';
        $.each(SurveyTypes.list(), function (_, t) {
            menu += '<li><button class="dropdown-item" type="button" data-add-type="' + t.code + '">' +
                '<i class="bi ' + t.icon + ' me-2"></i>' + esc(t.label) + '</button></li>';
            palette += '<button type="button" class="sv-type-item" data-add-type="' + t.code + '">' +
                '<span class="sv-type-icon"><i class="bi ' + t.icon + '"></i></span>' +
                '<span class="sv-type-text"><strong>' + esc(t.label) + '</strong><small>' + esc(t.description) + '</small></span>' +
                '</button>';
        });
        $('#addQuestionMenu').html(menu);
        $('#typePalette').html(palette);
    }

    function addQuestion(type) {
        if (!Store.isEditable()) return;
        Store.add(type, Store.state.selectedId);
        // En pantallas angostas llevar al editor y enfocar el texto.
        setTimeout(function () {
            var $text = $('#questionEditor [data-field="text"]');
            if ($text.length) {
                $text.trigger('focus');
                if (window.innerWidth < 992) $('#questionEditor')[0].scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        }, 50);
    }

    // ---------------------------------------------------------------------
    // Vista previa de la pregunta seleccionada
    // ---------------------------------------------------------------------

    function renderQuestionPreview() {
        var q = Store.selected();
        var $box = $('#questionPreview').empty();
        if (!q) {
            $box.html('<p class="text-muted small mb-0">Selecciona una pregunta para ver cómo se mostrará.</p>');
            return;
        }
        var label = SurveyLogic.questionLabel(Store.questions(), q.id).substring(1);
        $box.append(SurveyRenderer.renderQuestion(q, label, { noShuffle: true }));
        if (q.randomizeOptions) $box.append('<div class="small text-muted mt-2"><i class="bi bi-shuffle me-1"></i>Las opciones se mostrarán en orden aleatorio.</div>');

        var incoming = SurveyLogic.incomingRules(q.id, Store.questions());
        if (incoming.length) {
            var html = '<div class="sv-preview-cond"><div class="small fw-semibold mb-1"><i class="bi bi-signpost-split me-1"></i>Condiciones que afectan a esta pregunta</div>';
            $.each(incoming, function (_, x) {
                html += '<div class="small">' + esc(SurveyLogic.questionLabel(Store.questions(), x.source.id)) + ': ' +
                    SurveyLogic.describeRule(x.rule, x.source, Store.questions()) + '</div>';
            });
            $box.append(html + '</div>');
        }
    }

    // ---------------------------------------------------------------------
    // Resumen de lógica condicional
    // ---------------------------------------------------------------------

    function renderLogicSummary() {
        var questions = Store.questions();
        var html = '';
        $.each(questions, function (i, q) {
            if (!(q.rules || []).length) return;
            html += '<div class="sv-logic-group" data-qid="' + esc(q.id) + '">' +
                '<div class="sv-logic-source"><span class="sv-chip sv-chip-dark">P' + (i + 1) + '</span> ' + esc(q.text || 'Sin texto') + '</div><ul>';
            $.each(q.rules, function (_, r) {
                html += '<li>' + SurveyLogic.describeRule(r, q, questions) + '</li>';
            });
            html += '</ul></div>';
        });

        var warnings = Store.logicWarnings();
        if (warnings.length) {
            html += '<div class="alert alert-warning small mb-0 mt-2"><i class="bi bi-exclamation-triangle me-1"></i>' + $.map(warnings, esc).join('<br>') + '</div>';
        }
        $('#logicSummary').html(html || '<p class="small text-muted mb-0">Sin reglas. Agrega lógica desde «Lógica condicional» en el panel de cada pregunta.</p>');
    }

    // ---------------------------------------------------------------------
    // Encabezado y estado de solo lectura
    // ---------------------------------------------------------------------

    function renderHeader() {
        var survey = Store.survey();
        var v = Store.version();
        if (!survey || !v) return;
        var info = statusInfo(v.status);
        var editable = Store.isEditable();
        var draft = Store.draftVersion();

        if (!$('#surveyNameInput').is(':focus')) $('#surveyNameInput').val(survey.name);
        fitTitle();
        $('#surveyNameInput').prop('readonly', !editable);
        $('#statusBadge').attr('class', 'badge sv-status ' + info.css).html('<i class="bi ' + info.icon + '"></i> ' + info.label);
        $('#versionLabel').text('Versión ' + v.number);
        $('#btnSaveDraft').prop('disabled', !editable);
        $('#btnPublish').prop('disabled', v.status !== 'Draft');
        $('#btnCloseSurvey').prop('disabled', v.status !== 'Published');
        $('#btnArchive').prop('disabled', v.status === 'Archived' || v.status === 'Draft');
        $('body').toggleClass('sv-readonly', !editable);
        $('.sv-mock-only').toggleClass('d-none', !SurveyApi.isMock);

        var $banner = $('#readOnlyBanner').toggleClass('d-none', editable);
        if (!editable) {
            var reason = v.responseCount
                ? 'La versión ' + v.number + ' está <strong>' + info.label.toLowerCase() + '</strong> y tiene <strong>' + U.count(v.responseCount, 'respuesta') + '</strong>. No se sobrescribe para conservar el histórico.'
                : 'La versión ' + v.number + ' está <strong>' + info.label.toLowerCase() + '</strong> y es de solo lectura.';
            $('#readOnlyText').html(reason);
            $('#btnNewVersion').html(draft
                ? '<i class="bi bi-arrow-right"></i> Ir al borrador v' + draft.number
                : '<i class="bi bi-plus-lg"></i> Editar como nueva versión');
        }
        $banner.find('#btnNewVersion').attr('data-draft-id', draft ? draft.id : '');

        $('#questionEditorFieldset, #settingsFieldset').prop('disabled', !editable);
    }

    /** El campo del nombre mide exactamente lo que su texto (con la fuente real), sin cortarlo. */
    var measureCtx = null;
    function fitTitle() {
        var el = document.getElementById('surveyNameInput');
        if (!el) return;
        measureCtx = measureCtx || document.createElement('canvas').getContext('2d');
        var style = window.getComputedStyle(el);
        measureCtx.font = style.fontWeight + ' ' + style.fontSize + ' ' + style.fontFamily;
        var text = el.value || el.placeholder || '';
        el.style.width = Math.ceil(measureCtx.measureText(text).width + 12) + 'px';
    }

    function renderDirty(dirty) {
        $('#dirtyIndicator').toggleClass('d-none', !dirty);
    }

    // ---------------------------------------------------------------------
    // Historial de versiones
    // ---------------------------------------------------------------------

    function renderVersions() {
        var current = Store.version();
        var draft = Store.draftVersion();
        var html = '';
        $.each(Store.versions(), function (_, v) {
            var info = statusInfo(v.status);
            var isCurrent = current && v.id === current.id;
            html += '<div class="sv-version' + (isCurrent ? ' current' : '') + '">' +
                '<div class="d-flex justify-content-between align-items-start gap-2">' +
                '  <div><div class="fw-semibold">Versión ' + v.number + (isCurrent ? ' <span class="small text-muted">(abierta)</span>' : '') + '</div>' +
                '  <div class="small text-muted">Creada ' + esc(U.formatDate(v.createdAt)) +
                (v.publishedAt ? ' · Publicada ' + esc(U.formatDate(v.publishedAt)) : '') + '</div></div>' +
                '  <span class="badge sv-status ' + info.css + '"><i class="bi ' + info.icon + '"></i> ' + info.label + '</span>' +
                '</div>' +
                '<div class="small mt-1"><i class="bi bi-people me-1"></i>' + U.count(v.responseCount, 'respuesta') + ' · ' + U.count(v.questions.length, 'pregunta') + '</div>' +
                '<div class="d-flex gap-2 mt-2">' +
                (isCurrent ? '' : '<button type="button" class="btn btn-sm btn-outline-dark" data-open-version="' + esc(v.id) + '"><i class="bi bi-box-arrow-in-right"></i> Abrir</button>') +
                (!draft ? '<button type="button" class="btn btn-sm btn-light border" data-copy-version="' + esc(v.id) + '"><i class="bi bi-copy"></i> Crear versión a partir de esta</button>' : '') +
                '</div></div>';
        });
        $('#versionsList').html(html);
    }

    // ---------------------------------------------------------------------
    // Eventos
    // ---------------------------------------------------------------------

    function bind() {
        var $list = $('#questionList');

        $list.on('click', '.sv-q-item', function (e) {
            if ($(e.target).closest('.sv-q-menu').length) return;
            Store.select($(this).attr('data-qid'));
        });
        $list.on('keydown', '.sv-q-item', function (e) {
            if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); Store.select($(this).attr('data-qid')); }
        });
        $list.on('click', '[data-act]', function () {
            questionAction($(this).attr('data-act'), $(this).closest('.sv-q-item').attr('data-qid'));
        });
        U.sortable($list, '.sv-q-item', function (ids) { Store.reorder(ids); });

        $(document).on('click', '[data-add-type]', function () { addQuestion($(this).attr('data-add-type')); });

        $('#surveyNameInput').on('input', function () {
            fitTitle();
            if (Store.isEditable()) Store.setName($(this).val());
        });

        $('#logicSummary').on('click', '.sv-logic-group', function () {
            Store.select($(this).attr('data-qid'));
        });

        Store.on('survey:loaded', function () {
            renderHeader(); renderList(); renderQuestionPreview(); renderLogicSummary(); renderVersions();
        });
        Store.on('questions:changed', function () {
            renderList(); renderLogicSummary(); renderVersions();
        });
        Store.on('question:updated', function () {
            renderList(); renderQuestionPreview(); renderLogicSummary();
        });
        Store.on('selection:changed', function () {
            $list.find('.sv-q-item').each(function () {
                $(this).toggleClass('active', $(this).attr('data-qid') === Store.state.selectedId);
            });
            renderQuestionPreview();
        });
        Store.on('dirty:changed', renderDirty);
    }

    function init() {
        renderTypeMenus();
        bind();
    }

    return {
        init: init,
        statusInfo: statusInfo,
        questionAction: questionAction,
        renderHeader: renderHeader,
        renderVersions: renderVersions
    };
})(jQuery);
