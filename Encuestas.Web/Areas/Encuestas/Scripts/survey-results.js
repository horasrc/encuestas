/* =========================================================================
 * survey-results.js
 * Pestaña "Resultados". Recibe las respuestas crudas de SurveyApi.getResults
 * y las agrega en el navegador por tipo de pregunta.
 *
 * NOTA: para volúmenes grandes conviene que el servidor devuelva los
 * agregados ya calculados (GROUP BY sobre QuestionResponse, ver ejemplo en
 * Database/schema.sql) y que este módulo solo los dibuje.
 * ========================================================================= */
var SurveyResults = (function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var Store = SurveyStore;
    var OTHER = SurveyRenderer.OTHER;
    var current = { version: null, data: null };

    // ---------------------------------------------------------------------
    // Selección de versión
    // ---------------------------------------------------------------------

    function renderVersionSelect() {
        var versions = $.grep(Store.versions(), function (v) { return v.status !== 'Draft' || v.responseCount; });
        var $sel = $('#resultsVersion');
        var selected = $sel.val();
        if (!versions.length) {
            $sel.html('<option value="">Sin versiones publicadas</option>');
            return null;
        }
        var preferred = $.grep(versions, function (v) { return v.id === selected; })[0] ||
            $.grep(versions, function (v) { return v.status === 'Published'; })[0] || versions[0];
        $sel.html($.map(versions, function (v) {
            return '<option value="' + esc(v.id) + '"' + (v.id === preferred.id ? ' selected' : '') + '>Versión ' + v.number + ' · ' +
                esc(SurveyBuilder.statusInfo(v.status).label) + ' · ' + U.count(v.responseCount, 'respuesta') + '</option>';
        }).join(''));
        return preferred;
    }

    function load() {
        var version = renderVersionSelect();
        if (!version) {
            $('#resultsKpis').empty();
            $('#resultsQuestions').html('<div class="col-12"><div class="sv-empty"><i class="bi bi-bar-chart"></i><p>Publica la encuesta para empezar a recibir respuestas.</p></div></div>');
            return;
        }
        $('#resultsQuestions').html('<div class="col-12 text-center py-5"><div class="spinner-border text-secondary" role="status"><span class="visually-hidden">Cargando…</span></div></div>');
        SurveyApi.getResults(Store.survey().id, version.id).done(function (data) {
            current.version = version;
            current.data = data;
            render();
        }).fail(function () {
            $('#resultsQuestions').html('<div class="col-12"><div class="alert alert-danger">No fue posible cargar los resultados.</div></div>');
        });
    }

    // ---------------------------------------------------------------------
    // KPIs
    // ---------------------------------------------------------------------

    function kpi(icon, label, value, extra) {
        return '<div class="col"><div class="card sv-card sv-kpi h-100"><div class="card-body">' +
            '<div class="sv-kpi-label"><i class="bi ' + icon + ' me-1"></i>' + esc(label) + '</div>' +
            '<div class="sv-kpi-value">' + value + '</div>' + (extra || '') + '</div></div></div>';
    }

    function renderKpis() {
        var d = current.data;
        var total = d.responses.length;
        var rate = U.percent(total, d.audience);
        var pending = Math.max(d.audience - total, 0);
        var daysLeft = '';
        if (d.endDate) {
            var diff = Math.ceil((new Date(d.endDate + 'T23:59:59') - new Date()) / 86400000);
            daysLeft = '<div class="small text-muted">' + (diff >= 0 ? 'Faltan ' + diff + ' días' : 'Finalizó') + '</div>';
        }
        $('#resultsKpis').html(
            kpi('bi-people', 'Total de respuestas', total.toLocaleString('es-MX')) +
            kpi('bi-graph-up-arrow', 'Tasa de respuesta', rate + '%',
                '<div class="progress sv-kpi-progress" role="progressbar" aria-label="Tasa de respuesta" aria-valuenow="' + rate + '" aria-valuemin="0" aria-valuemax="100"><div class="progress-bar" style="width:' + Math.min(rate, 100) + '%"></div></div>') +
            kpi('bi-hourglass-split', 'Respuestas pendientes', pending.toLocaleString('es-MX'), '<div class="small text-muted">de ' + d.audience.toLocaleString('es-MX') + ' en la audiencia</div>') +
            kpi('bi-calendar-event', 'Fecha de inicio', esc(U.formatDate(d.startDate))) +
            kpi('bi-calendar-check', 'Fecha de cierre', esc(U.formatDate(d.endDate)), daysLeft)
        );
    }

    // ---------------------------------------------------------------------
    // Componentes visuales
    // ---------------------------------------------------------------------

    /** Barras horizontales. rows: [{ label, count, sub }], base = total para el %. */
    function bars(rows, base) {
        var max = 0;
        $.each(rows, function (_, r) { max = Math.max(max, r.count); });
        return '<div class="sv-bars">' + $.map(rows, function (r) {
            var pct = U.percent(r.count, base);
            return '<div class="sv-bar-row' + (r.count === max && max > 0 ? ' top' : '') + '">' +
                '<div class="sv-bar-label" title="' + esc(r.label) + '">' + esc(r.label) + (r.sub ? '<small>' + esc(r.sub) + '</small>' : '') + '</div>' +
                '<div class="sv-bar-track"><div class="sv-bar-fill" style="width:' + (max ? (r.count / max * 100) : 0) + '%"></div></div>' +
                '<div class="sv-bar-value"><strong>' + r.count + '</strong><span>' + pct + '%</span></div>' +
                '</div>';
        }).join('') + '</div>';
    }

    function stat(label, value) {
        return '<div class="col"><div class="sv-stat"><div class="small text-muted">' + esc(label) + '</div><div class="fs-5 fw-semibold">' + esc(value) + '</div></div></div>';
    }

    function answersFor(q) {
        var list = [];
        $.each(current.data.responses, function (_, r) {
            var a = r.answers[q.id];
            if (a && !SurveyLogic.isEmpty(a)) list.push({ answer: a, response: r });
        });
        return list;
    }

    // ---------------------------------------------------------------------
    // Resumen por tipo
    // ---------------------------------------------------------------------

    function choiceSummary(q, items) {
        var counts = {};
        var otherTexts = [];
        $.each(items, function (_, it) {
            $.each(it.answer.optionIds || [], function (_, id) { counts[id] = (counts[id] || 0) + 1; });
            if (it.answer.otherText) otherTexts.push(it.answer.otherText);
        });
        var rows = $.map(q.options, function (o) { return { label: o.text, count: counts[o.id] || 0 }; });
        if (q.allowOther || counts[OTHER]) rows.push({ label: q.otherLabel || 'Otro', count: counts[OTHER] || 0 });

        var html = bars(rows, items.length);
        if (q.type === 'multipleChoice') {
            html += '<div class="small text-muted mt-2"><i class="bi bi-info-circle me-1"></i>El porcentaje es sobre ' + U.count(items.length, 'persona') + '; cada una pudo elegir varias opciones.</div>';
        }
        if (otherTexts.length) {
            var id = 'other_' + q.id;
            html += '<button class="btn btn-sm sv-link-btn mt-2" type="button" data-bs-toggle="collapse" data-bs-target="#' + id + '" aria-expanded="false">' +
                '<i class="bi bi-chat-left-text"></i> Ver respuestas «Otro» (' + otherTexts.length + ')</button>' +
                '<div class="collapse" id="' + id + '"><ul class="sv-text-list mt-2">' +
                $.map(otherTexts, function (t) { return '<li>' + esc(t) + '</li>'; }).join('') + '</ul></div>';
        }
        return html;
    }

    function scaleSummary(q, items) {
        var v = q.validation || {};
        var min = Number(v.scaleMin || 1), max = Number(v.scaleMax || 5);
        var counts = {}, sum = 0;
        $.each(items, function (_, it) { counts[it.answer.number] = (counts[it.answer.number] || 0) + 1; sum += Number(it.answer.number); });
        var rows = [];
        for (var n = max; n >= min; n--) {
            rows.push({ label: String(n) + (n === max && v.scaleMaxLabel ? ' · ' + v.scaleMaxLabel : n === min && v.scaleMinLabel ? ' · ' + v.scaleMinLabel : ''), count: counts[n] || 0 });
        }
        var avg = items.length ? (sum / items.length).toFixed(1) : '—';
        return '<div class="sv-avg"><span class="sv-avg-value">' + avg + '</span><span class="text-muted small">promedio de ' + min + ' a ' + max + '</span></div>' + bars(rows, items.length);
    }

    function numberSummary(q, items) {
        var nums = $.map(items, function (it) { return Number(it.answer.number); }).sort(function (a, b) { return a - b; });
        if (!nums.length) return '';
        var sum = 0;
        $.each(nums, function (_, n) { sum += n; });
        var mid = Math.floor(nums.length / 2);
        var median = nums.length % 2 ? nums[mid] : (nums[mid - 1] + nums[mid]) / 2;
        return '<div class="row row-cols-2 row-cols-sm-4 g-2">' +
            stat('Promedio', (sum / nums.length).toFixed(1)) + stat('Mediana', median) + stat('Mínimo', nums[0]) + stat('Máximo', nums[nums.length - 1]) +
            '</div>';
    }

    function dateSummary(q, items) {
        var dates = $.map(items, function (it) { return it.answer.date; }).sort();
        var byMonth = {};
        var months = ['ene', 'feb', 'mar', 'abr', 'may', 'jun', 'jul', 'ago', 'sep', 'oct', 'nov', 'dic'];
        $.each(dates, function (_, d) { var k = d.substring(0, 7); byMonth[k] = (byMonth[k] || 0) + 1; });
        var rows = $.map(Object.keys(byMonth).sort(), function (k) {
            return { label: months[Number(k.substring(5, 7)) - 1] + ' ' + k.substring(0, 4), count: byMonth[k] };
        });
        return '<div class="row row-cols-2 g-2 mb-3">' + stat('Fecha más antigua', U.formatDate(dates[0])) + stat('Fecha más reciente', U.formatDate(dates[dates.length - 1])) + '</div>' +
            bars(rows, items.length);
    }

    function textSummary(q, items) {
        var id = 'txt_' + q.id;
        return '<div class="input-group input-group-sm mb-2">' +
            '<span class="input-group-text"><i class="bi bi-search"></i></span>' +
            '<input type="search" class="form-control" data-text-search="' + esc(id) + '" placeholder="Buscar en ' + U.count(items.length, 'respuesta') + '…" aria-label="Buscar respuestas">' +
            '</div>' +
            '<ul class="sv-text-list sv-text-scroll" id="' + esc(id) + '">' +
            $.map(items, function (it) {
                return '<li data-text="' + esc(it.answer.text.toLowerCase()) + '"><span>' + esc(it.answer.text) + '</span>' +
                    '<small class="text-muted">' + esc(U.formatDate(it.response.submittedAt)) + '</small></li>';
            }).join('') +
            '</ul><div class="small text-muted" data-text-count="' + esc(id) + '"></div>';
    }

    function matrixSummary(q, items) {
        var m = q.matrix;
        var counts = {};
        $.each(items, function (_, it) {
            $.each(it.answer.matrix || {}, function (rowId, cols) {
                $.each(cols, function (_, colId) { counts[rowId + '|' + colId] = (counts[rowId + '|' + colId] || 0) + 1; });
            });
        });
        var html = '<div class="table-responsive"><table class="table table-sm sv-matrix-results align-middle mb-0"><thead><tr><th scope="col">Aspecto</th>';
        $.each(m.columns, function (_, c) { html += '<th scope="col" class="text-center">' + esc(c.text) + '</th>'; });
        html += '</tr></thead><tbody>';
        $.each(m.rows, function (_, r) {
            var rowTotal = 0;
            $.each(m.columns, function (_, c) { rowTotal += counts[r.id + '|' + c.id] || 0; });
            html += '<tr><th scope="row">' + esc(r.text) + '<small class="d-block text-muted fw-normal">' + rowTotal + ' resp.</small></th>';
            $.each(m.columns, function (_, c) {
                var n = counts[r.id + '|' + c.id] || 0;
                var pct = U.percent(n, rowTotal);
                html += '<td class="text-center sv-heat" style="--sv-heat:' + (pct / 100).toFixed(2) + '">' +
                    '<strong>' + pct + '%</strong><small class="d-block">' + n + '</small></td>';
            });
            html += '</tr>';
        });
        return html + '</tbody></table></div>';
    }

    function dependentSummary(q, items) {
        var d = q.dependent;
        var counts = {};
        var byId = {};
        $.each(d.options, function (_, o) { byId[o.id] = o; });
        $.each(items, function (_, it) { $.each(it.answer.optionIds || [], function (_, id) { counts[id] = (counts[id] || 0) + 1; }); });

        var html = '';
        $.each(d.levels, function (level, lv) {
            var rows = $.map($.grep(d.options, function (o) { return o.level === level && counts[o.id]; }), function (o) {
                var parent = o.parentId && byId[o.parentId] ? byId[o.parentId].text : '';
                return { label: o.text, sub: parent, count: counts[o.id] };
            }).sort(function (a, b) { return b.count - a.count; }).slice(0, 8);
            html += '<div class="sv-dep-result"><div class="fw-semibold small mb-1">' + esc(lv.name) + '</div>' +
                (rows.length ? bars(rows, items.length) : '<div class="small text-muted">Sin selecciones.</div>') + '</div>';
        });
        return html;
    }

    var summarizers = {
        singleChoice: choiceSummary,
        multipleChoice: choiceSummary,
        dropdown: choiceSummary,
        yesNo: choiceSummary,
        scale: scaleSummary,
        number: numberSummary,
        date: dateSummary,
        text: textSummary,
        matrix: matrixSummary,
        dependent: dependentSummary
    };

    function render() {
        renderKpis();
        var questions = SurveyLogic.sorted(current.version.questions);
        var html = '';
        $.each(questions, function (i, q) {
            var type = SurveyTypes.get(q.type);
            var items = answersFor(q);
            var wide = q.type === 'matrix' || q.type === 'dependent' || q.type === 'text';
            html += '<div class="col-12' + (wide ? '' : ' col-xl-6') + '"><div class="card sv-card h-100">' +
                '<div class="card-header sv-card-header">' +
                '  <span><span class="sv-chip sv-chip-dark">P' + (i + 1) + '</span> <i class="bi ' + type.icon + ' ms-1"></i> ' + esc(type.label) + '</span>' +
                '  <span class="small text-muted">' + U.count(items.length, 'respuesta') + '</span>' +
                '</div><div class="card-body">' +
                '<h3 class="sv-result-title">' + esc(q.text) + '</h3>' +
                (items.length ? summarizers[q.type](q, items) : '<p class="small text-muted mb-0">Sin respuestas todavía (puede estar oculta por la lógica condicional).</p>') +
                '</div></div></div>';
        });
        $('#resultsQuestions').html(html);
    }

    // ---------------------------------------------------------------------
    // Exportar CSV (una fila por respuesta, una columna por pregunta)
    // ---------------------------------------------------------------------

    var answerToText = SurveyLogic.answerText;

    function exportCsv() {
        if (!current.data) return;
        var questions = SurveyLogic.sorted(current.version.questions);
        var cell = function (v) { return '"' + String(v === null || v === undefined ? '' : v).replace(/"/g, '""') + '"'; };
        var lines = [[cell('Respuesta'), cell('Fecha')].concat($.map(questions, function (q, i) { return cell('P' + (i + 1) + ' ' + q.text); })).join(',')];
        $.each(current.data.responses, function (_, r) {
            lines.push([cell(r.id), cell(r.submittedAt)].concat($.map(questions, function (q) { return cell(answerToText(q, r.answers[q.id])); })).join(','));
        });
        var blob = new Blob(['﻿' + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
        var a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = 'resultados-' + (Store.survey().name || 'encuesta').replace(/[^\w\-]+/g, '_') + '-v' + current.version.number + '.csv';
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { URL.revokeObjectURL(a.href); a.remove(); }, 0);
    }

    // ---------------------------------------------------------------------
    // Eventos
    // ---------------------------------------------------------------------

    function bind() {
        $('#tabResultsBtn').on('shown.bs.tab', load);
        $('#resultsVersion').on('change', load);
        $('#btnExportCsv').on('click', exportCsv);

        $('#resultsQuestions').on('input', '[data-text-search]', function () {
            var term = $.trim($(this).val()).toLowerCase();
            var id = $(this).attr('data-text-search');
            var visible = 0;
            $('#' + id + ' li').each(function () {
                var match = !term || $(this).attr('data-text').indexOf(term) >= 0;
                $(this).toggleClass('d-none', !match);
                if (match) visible += 1;
            });
            $('[data-text-count="' + id + '"]').text(term ? visible + ' coincidencias' : '');
        });
    }

    function init() {
        bind();
    }

    return { init: init, load: load };
})(jQuery);
