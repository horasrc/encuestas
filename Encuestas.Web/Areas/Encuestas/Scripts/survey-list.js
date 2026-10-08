/* =========================================================================
 * survey-list.js
 * Pantalla /Encuestas: listado con filtro por estado y búsqueda, y modal
 * "Nueva encuesta" (crea la versión 1 en borrador y abre el constructor).
 * ========================================================================= */
(function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var Api = SurveyApi;
    var rows = [];
    var filter = { status: '', term: '' };

    function render() {
        // Contadores por estado.
        $('[data-count]').each(function () {
            var st = $(this).attr('data-count');
            $(this).text($.grep(rows, function (r) { return !st || r.status === st; }).length);
        });

        var term = filter.term.toLowerCase();
        var visible = $.grep(rows, function (r) {
            return (!filter.status || r.status === filter.status) &&
                (!term || (r.name + ' ' + (r.description || '')).toLowerCase().indexOf(term) >= 0);
        });

        var $body = $('#surveyListBody');
        if (!rows.length) {
            $body.html('<tr><td colspan="6"><div class="sv-empty"><i class="bi bi-clipboard2-plus"></i>' +
                '<p>Aún no hay encuestas.<br>Crea la primera con «Nueva encuesta».</p></div></td></tr>');
            return;
        }
        if (!visible.length) {
            $body.html('<tr><td colspan="6"><div class="sv-empty"><i class="bi bi-search"></i><p>Ninguna encuesta coincide con el filtro.</p></div></td></tr>');
            return;
        }

        $body.html($.map(visible, function (r) {
            var edit = Api.url('constructor', r.id);
            return '<tr>' +
                '<td class="sv-list-name"><a href="' + esc(edit) + '" class="fw-semibold">' + esc(r.name) + '</a>' +
                (r.description ? '<small class="d-block text-muted">' + esc(r.description) + '</small>' : '') + '</td>' +
                '<td>' + U.statusBadge(r.status) +
                (r.draftVersion ? '<small class="d-block text-muted mt-1"><i class="bi bi-pencil me-1"></i>Borrador v' + r.draftVersion + ' en edición</small>' : '') + '</td>' +
                '<td class="d-none d-md-table-cell">v' + r.currentVersion + '<small class="d-block text-muted">' + r.versionCount + (r.versionCount === 1 ? ' versión' : ' versiones') + '</small></td>' +
                '<td class="text-end"><strong>' + r.responseCount + '</strong>' +
                (r.totalResponses !== r.responseCount ? '<small class="d-block text-muted">' + r.totalResponses + ' en total</small>' : '') + '</td>' +
                '<td class="d-none d-lg-table-cell small">' + esc(U.formatDate(r.startDate)) + ' – ' + esc(U.formatDate(r.endDate)) + '</td>' +
                '<td class="text-end text-nowrap">' +
                '  <a class="btn btn-sm btn-outline-dark" href="' + esc(edit) + '"><i class="bi bi-pencil"></i><span class="d-none d-xl-inline"> Editar</span></a>' +
                '  <div class="dropdown d-inline-block">' +
                '    <button class="btn btn-sm btn-light border" type="button" data-bs-toggle="dropdown" aria-expanded="false" aria-label="Más acciones"><i class="bi bi-three-dots-vertical"></i></button>' +
                '    <ul class="dropdown-menu dropdown-menu-end shadow-sm">' +
                '      <li><a class="dropdown-item" href="' + esc(edit) + '#resultados"><i class="bi bi-bar-chart me-2"></i>Resultados</a></li>' +
                (r.status === 'Published'
                    ? '<li><a class="dropdown-item" href="' + esc(Api.url('respond', r.id)) + '" target="_blank" rel="noopener"><i class="bi bi-box-arrow-up-right me-2"></i>Página de respuesta</a></li>'
                    : '') +
                '      <li><button type="button" class="dropdown-item" data-duplicate="' + esc(r.id) + '"><i class="bi bi-copy me-2"></i>Duplicar</button></li>' +
                (r.totalResponses === 0
                    ? '<li><hr class="dropdown-divider"></li><li><button type="button" class="dropdown-item text-danger" data-delete="' + esc(r.id) + '"><i class="bi bi-trash me-2"></i>Eliminar</button></li>'
                    : '') +
                '    </ul>' +
                '  </div>' +
                '</td>' +
                '</tr>';
        }).join(''));
    }

    function load() {
        Api.list().done(function (data) {
            rows = data.sort(function (a, b) { return a.name.localeCompare(b.name, 'es'); });
            render();
        }).fail(function () {
            $('#surveyListBody').html('<tr><td colspan="6"><div class="alert alert-danger m-3">No fue posible cargar las encuestas.</div></td></tr>');
        });
    }

    function create(e) {
        e.preventDefault();
        var $name = $('#newSurveyName');
        var name = $.trim($name.val());
        $name.toggleClass('is-invalid', !name);
        if (!name) { $name.trigger('focus'); return; }

        var $btn = $('#btnCreateSurvey').prop('disabled', true);
        Api.createSurvey(name, $.trim($('#newSurveyDescription').val())).done(function (result) {
            if (!result.ok) {
                $btn.prop('disabled', false);
                U.toast(result.message, 'danger');
                return;
            }
            window.location.href = Api.url('constructor', result.data.id);
        });
    }

    function find(id) {
        return $.grep(rows, function (r) { return r.id === id; })[0];
    }

    function duplicate(id) {
        var r = find(id);
        U.confirmDialog({
            title: 'Duplicar encuesta',
            message: '<p>Se creará <strong>«Copia de ' + esc(r.name) + '»</strong> como borrador, con las mismas preguntas, reglas y configuración. No se copian respuestas.</p>',
            confirmText: 'Duplicar y abrir'
        }).done(function () {
            Api.duplicateSurvey(id).done(function (result) {
                if (!result.ok) { U.toast(result.message, 'danger'); return; }
                window.location.href = Api.url('constructor', result.data.id);
            });
        });
    }

    function remove(id) {
        var r = find(id);
        U.confirmDialog({
            title: 'Eliminar encuesta',
            message: '<p>¿Eliminar <strong>«' + esc(r.name) + '»</strong>? Esta encuesta no tiene respuestas.</p>',
            confirmText: 'Eliminar',
            danger: true
        }).done(function () {
            Api.deleteSurvey(id).done(function (result) {
                if (!result.ok) { U.toast(result.message, 'danger'); return; }
                U.toast(result.message, 'info');
                load();
            });
        });
    }

    $(function () {
        $('#surveyListBody').on('click', '[data-duplicate]', function () { duplicate($(this).attr('data-duplicate')); });
        $('#surveyListBody').on('click', '[data-delete]', function () { remove($(this).attr('data-delete')); });
        $('#statusFilter').on('click', '[data-status]', function () {
            $('#statusFilter .nav-link').removeClass('active');
            $(this).addClass('active');
            filter.status = $(this).attr('data-status');
            render();
        });
        $('#surveySearch').on('input', function () { filter.term = $.trim($(this).val()); render(); });
        $('#newSurveyForm').on('submit', create);
        $('#newSurveyName').on('input', function () { $(this).removeClass('is-invalid'); });
        $('#newSurveyModal').on('shown.bs.modal', function () { $('#newSurveyName').trigger('focus'); });
        load();
    });
})(jQuery);
