/* =========================================================================
 * survey-pending.js
 * Pantalla "Encuestas por responder" (Views/Encuestas/Pendientes.cshtml):
 * encuestas publicadas del segmento que la persona puede contestar ahora
 * (SurveyApi.pending). Qué encuestas le tocan lo decide el portal.
 * ========================================================================= */
(function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var Api = SurveyApi;

    function render(rows) {
        var $list = $('#pendingList');
        if (!rows.length) {
            $list.html('<div class="col-12"><div class="sv-empty"><i class="bi bi-check2-all"></i>' +
                '<p>No tienes encuestas pendientes.<br>Cuando haya una nueva para ti, aparecerá aquí.</p></div></div>');
            return;
        }
        $list.html($.map(rows, function (r) {
            return '<div class="col-12 col-md-6 col-xl-4"><div class="card sv-card h-100"><div class="card-body d-flex flex-column">' +
                '<h2 class="h6 fw-bold mb-1">' + esc(r.name) + '</h2>' +
                (r.description ? '<p class="small text-muted mb-2">' + esc(r.description) + '</p>' : '') +
                (r.endDate ? '<p class="small mb-3"><i class="bi bi-calendar-check me-1"></i>Disponible hasta ' + esc(U.formatDate(r.endDate)) + '</p>' : '<div class="mb-3"></div>') +
                '<a class="btn btn-accent mt-auto align-self-start" href="' + esc(Api.url('respond', r.id)) + '"><i class="bi bi-pencil-square me-1"></i>Responder</a>' +
                '</div></div></div>';
        }).join(''));
    }

    $(function () {
        Api.pending().done(function (rows) {
            render((rows || []).sort(function (a, b) { return a.name.localeCompare(b.name, 'es'); }));
        }).fail(function () {
            $('#pendingList').html('<div class="col-12"><div class="alert alert-danger">No fue posible cargar las encuestas.</div></div>');
        });
    });
})(jQuery);
