/* =========================================================================
 * survey-settings.js
 * Pestaña "Configuración": publicación (fechas, respuestas por usuario,
 * anonimato, autenticación, mensajes) y segmentación de la audiencia.
 * Los catálogos (roles, segmentos, regiones, sucursales, usuarios) llegan
 * de SurveyApi.getCatalogs() y se guardan en SurveyStore.state.catalogs.
 * ========================================================================= */
var SurveySettings = (function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var Store = SurveyStore;

    var DIMS = [
        { key: 'roles', title: 'Rol', hint: 'Ejecutivos, gerentes…', icon: 'bi-person-badge' },
        { key: 'segments', title: 'Segmento', hint: 'Banca Personal, Premier…', icon: 'bi-diagram-2' },
        { key: 'regions', title: 'Región', hint: '', icon: 'bi-geo-alt' },
        { key: 'branches', title: 'Sucursal', hint: '', icon: 'bi-building' },
        { key: 'users', title: 'Usuarios específicos', hint: 'Se incluyen siempre', icon: 'bi-person-plus' }
    ];

    function catalogs() {
        return Store.state.catalogs || { roles: [], segments: [], regions: [], branches: [], users: [] };
    }

    function seg() {
        var v = Store.version();
        return (v && v.segmentation) || { mode: 'all', roles: [], segments: [], regions: [], branches: [], users: [] };
    }

    function nameOf(list, id) {
        var item = $.grep(list, function (x) { return x.id === id; })[0];
        return item ? item.name : id;
    }

    // ---------------------------------------------------------------------
    // Render
    // ---------------------------------------------------------------------

    function render() {
        var v = Store.version();
        if (!v) return;
        var s = v.settings || {};

        $('[data-setting]').each(function () {
            var key = $(this).attr('data-setting');
            if (this.type === 'checkbox') this.checked = !!s[key];
            else $(this).val(s[key] || '');
        });
        $('#setRespOne').prop('checked', !s.allowMultipleResponses);
        $('#setRespMulti').prop('checked', !!s.allowMultipleResponses);

        var sg = seg();
        $('#segAll').prop('checked', sg.mode !== 'segmented');
        $('#segSegmented').prop('checked', sg.mode === 'segmented');

        renderSegmentation();
        refreshDerived();
    }

    function renderSegmentation() {
        var sg = seg();
        var html = '';
        $.each(DIMS, function (i, d) {
            var id = 'segAcc_' + d.key;
            html += '<div class="accordion-item">' +
                '<h2 class="accordion-header" id="' + id + 'H">' +
                '<button class="accordion-button collapsed" type="button" data-bs-toggle="collapse" data-bs-target="#' + id + '" aria-expanded="false" aria-controls="' + id + '">' +
                '<i class="bi ' + d.icon + ' me-2"></i>' + esc(d.title) +
                (d.hint ? '<span class="small text-muted ms-2 d-none d-sm-inline">' + esc(d.hint) + '</span>' : '') +
                '<span class="badge sv-badge-soft ms-auto me-2" data-seg-count="' + d.key + '"></span>' +
                '</button></h2>' +
                '<div id="' + id + '" class="accordion-collapse collapse" aria-labelledby="' + id + 'H">' +
                '<div class="accordion-body" data-seg-body="' + d.key + '"></div></div></div>';
        });
        $('#segAccordion').html(html);
        $.each(DIMS, function (_, d) { renderDim(d.key); });
        $('#segmentationPanel').toggleClass('d-none', sg.mode !== 'segmented');
    }

    function chips(dim, items, selected) {
        if (!items.length) return '<p class="small text-muted mb-0">No hay elementos que coincidan.</p>';
        var html = '<div class="sv-chip-group">';
        $.each(items, function (_, it) {
            var id = 'seg_' + dim + '_' + it.id;
            html += '<input type="checkbox" class="btn-check" id="' + esc(id) + '" data-seg-dim="' + dim + '" value="' + esc(it.id) + '"' +
                ($.inArray(it.id, selected) >= 0 ? ' checked' : '') + ' autocomplete="off">' +
                '<label class="btn btn-sm sv-chip-toggle" for="' + esc(id) + '">' + esc(it.name) + '</label>';
        });
        return html + '</div>';
    }

    function renderDim(dim) {
        var c = catalogs();
        var sg = seg();
        var $body = $('[data-seg-body="' + dim + '"]');
        var tools = '<div class="d-flex gap-3 small mb-2 sv-edit-only">' +
            '<button type="button" class="btn btn-link btn-sm p-0" data-seg-all="' + dim + '">Seleccionar todos</button>' +
            '<button type="button" class="btn btn-link btn-sm p-0" data-seg-none="' + dim + '">Limpiar</button></div>';

        if (dim === 'branches') {
            var term = ($('#segBranchSearch').val() || '').toLowerCase();
            var filtered = $.grep(c.branches, function (b) {
                return (!sg.regions.length || $.inArray(b.regionId, sg.regions) >= 0) &&
                    (!sg.segments.length || $.inArray(b.segmentId, sg.segments) >= 0) &&
                    (!term || b.name.toLowerCase().indexOf(term) >= 0);
            });
            var note = (sg.regions.length || sg.segments.length) ? '<div class="small text-muted mb-2"><i class="bi bi-funnel me-1"></i>Filtradas por la región y el segmento seleccionados.</div>' : '';
            if (!$body.find('#segBranchSearch').length) {
                $body.html('<input type="search" id="segBranchSearch" class="form-control form-control-sm mb-2" placeholder="Buscar sucursal…" aria-label="Buscar sucursal"><div data-seg-list></div>');
            }
            $body.find('[data-seg-list]').html(note + tools + chips(dim, filtered, sg.branches));
        } else if (dim === 'users') {
            var selected = $.map(sg.users, function (id) {
                var u = $.grep(c.users, function (x) { return x.id === id; })[0];
                return '<span class="sv-user-chip">' + esc(u ? u.name : id) +
                    '<button type="button" class="btn-close btn-close-sm sv-edit-only" data-remove-user="' + esc(id) + '" aria-label="Quitar"></button></span>';
            }).join('');
            $body.html(
                '<div class="position-relative sv-edit-only">' +
                '  <input type="search" id="segUserSearch" class="form-control form-control-sm" placeholder="Buscar por nombre o correo…" autocomplete="off" aria-label="Buscar usuario">' +
                '  <div id="segUserResults" class="list-group sv-user-results d-none"></div>' +
                '</div>' +
                '<div class="sv-user-selected mt-2">' + (selected || '<span class="small text-muted">Sin usuarios específicos.</span>') + '</div>');
        } else {
            $body.html(tools + chips(dim, c[dim] || [], sg[dim]));
        }
    }

    /** Contadores, resumen y audiencia estimada. */
    function refreshDerived() {
        var v = Store.version();
        if (!v) return;
        var s = v.settings || {};
        var sg = seg();
        var c = catalogs();

        $.each(DIMS, function (_, d) {
            var n = (sg[d.key] || []).length;
            $('[data-seg-count="' + d.key + '"]').text(n ? n : 'Todos').toggleClass('sv-badge-active', !!n);
        });
        $('[data-seg-count="users"]').text(sg.users.length || '0');

        var summary = '';
        if (sg.mode !== 'segmented') {
            summary = '<span class="sv-chip">Todos los colaboradores</span>';
        } else {
            $.each(['roles', 'segments', 'regions', 'branches'], function (_, k) {
                $.each(sg[k], function (_, id) { summary += '<span class="sv-chip">' + esc(nameOf(c[k], id)) + '</span>'; });
            });
            if (sg.users.length) summary += '<span class="sv-chip"><i class="bi bi-person"></i> +' + sg.users.length + '</span>';
        }
        $('#segSummary').html(summary);
        $('#audienceEstimate').text(SurveyApi.estimateAudience(sg, c).toLocaleString('es-MX'));

        var badDates = s.startDate && s.endDate && s.endDate < s.startDate;
        $('#setEndDate').toggleClass('is-invalid', !!badDates);
    }

    // ---------------------------------------------------------------------
    // Eventos
    // ---------------------------------------------------------------------

    function editable() { return Store.isEditable(); }

    function bind() {
        var $tab = $('#tabSettings');

        $tab.on('input change', '[data-setting]', function () {
            if (!editable()) return;
            var key = $(this).attr('data-setting');
            var value = this.type === 'checkbox' ? this.checked : $(this).val();
            Store.updateSettings(function (s) { s[key] = value; });
            refreshDerived();
        });

        $tab.on('change', 'input[name="setResponses"]', function () {
            var multi = $(this).val() === 'multi';
            Store.updateSettings(function (s) { s.allowMultipleResponses = multi; });
        });

        $tab.on('change', 'input[name="segMode"]', function () {
            var mode = $(this).val();
            Store.updateSettings(function (s, sg) { sg.mode = mode; });
            $('#segmentationPanel').toggleClass('d-none', mode !== 'segmented');
            refreshDerived();
        });

        $tab.on('change', '[data-seg-dim]', function () {
            var dim = $(this).attr('data-seg-dim');
            var id = $(this).val();
            var on = this.checked;
            Store.updateSettings(function (s, sg) {
                sg[dim] = $.grep(sg[dim], function (x) { return x !== id; });
                if (on) sg[dim].push(id);
            });
            if (dim === 'regions' || dim === 'segments') pruneBranches();
            refreshDerived();
        });

        $tab.on('click', '[data-seg-all], [data-seg-none]', function () {
            var all = $(this).is('[data-seg-all]');
            var dim = $(this).attr(all ? 'data-seg-all' : 'data-seg-none');
            var ids = $('[data-seg-body="' + dim + '"] [data-seg-dim]').map(function () { return this.value; }).get();
            Store.updateSettings(function (s, sg) { sg[dim] = all ? ids : []; });
            if (dim === 'regions' || dim === 'segments') pruneBranches();
            renderDim(dim);
            refreshDerived();
        });

        $tab.on('input', '#segBranchSearch', function () { renderDim('branches'); });

        // Usuarios específicos: búsqueda con resultados en lista.
        $tab.on('input focus', '#segUserSearch', function () {
            var term = $.trim($(this).val()).toLowerCase();
            var sg = seg();
            var $results = $('#segUserResults');
            if (term.length < 2) { $results.addClass('d-none'); return; }
            var matches = $.grep(catalogs().users, function (u) {
                return $.inArray(u.id, sg.users) < 0 && (u.name.toLowerCase().indexOf(term) >= 0 || u.email.toLowerCase().indexOf(term) >= 0);
            }).slice(0, 8);
            $results.html(matches.length ? $.map(matches, function (u) {
                return '<button type="button" class="list-group-item list-group-item-action" data-add-user="' + esc(u.id) + '">' +
                    '<div class="fw-semibold small">' + esc(u.name) + '</div><div class="small text-muted">' + esc(u.email) + ' · ' + esc(nameOf(catalogs().roles, u.roleId)) + '</div></button>';
            }).join('') : '<div class="list-group-item small text-muted">Sin coincidencias</div>').removeClass('d-none');
        });

        $tab.on('click', '[data-add-user]', function () {
            var id = $(this).attr('data-add-user');
            Store.updateSettings(function (s, sg) { if ($.inArray(id, sg.users) < 0) sg.users.push(id); });
            renderDim('users');
            refreshDerived();
            $('#segUserSearch').trigger('focus');
        });

        $tab.on('click', '[data-remove-user]', function () {
            var id = $(this).attr('data-remove-user');
            Store.updateSettings(function (s, sg) { sg.users = $.grep(sg.users, function (x) { return x !== id; }); });
            renderDim('users');
            refreshDerived();
        });

        $(document).on('click', function (e) {
            if (!$(e.target).closest('#segUserSearch, #segUserResults').length) $('#segUserResults').addClass('d-none');
        });

        Store.on('survey:loaded', render);
    }

    /** Si cambian región o segmento, se quitan sucursales que ya no aplican. */
    function pruneBranches() {
        var c = catalogs();
        Store.updateSettings(function (s, sg) {
            sg.branches = $.grep(sg.branches, function (id) {
                var b = $.grep(c.branches, function (x) { return x.id === id; })[0];
                return b && (!sg.regions.length || $.inArray(b.regionId, sg.regions) >= 0) &&
                    (!sg.segments.length || $.inArray(b.segmentId, sg.segments) >= 0);
            });
        });
        renderDim('branches');
    }

    function init() {
        bind();
    }

    return { init: init, render: render };
})(jQuery);
