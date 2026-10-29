/* =========================================================================
 * survey-settings.js
 * Pestaña "Configuración": publicación (fechas, respuestas por usuario,
 * anonimato, autenticación, mensajes). Quién responde y qué resultados ve
 * cada quien lo decide el portal (tramo de control), no esta pantalla.
 * ========================================================================= */
var SurveySettings = (function ($) {
    'use strict';

    var Store = SurveyStore;

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
        refreshDerived();
    }

    function refreshDerived() {
        var v = Store.version();
        if (!v) return;
        var s = v.settings || {};
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

        Store.on('survey:loaded', render);
    }

    function init() {
        bind();
    }

    return { init: init, render: render };
})(jQuery);
