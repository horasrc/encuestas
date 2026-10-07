/* =========================================================================
 * survey-preview.js
 * Pestaña "Vista previa". Dibuja la encuesta dentro de un iframe para que
 * los breakpoints de Bootstrap respondan al ancho real del dispositivo
 * (Desktop / Tablet / Móvil). Usa la versión en memoria, por lo que
 * incluye los cambios sin guardar.
 * ========================================================================= */
var SurveyPreview = (function ($) {
    'use strict';

    var Store = SurveyStore;

    /** Recursos que necesita el iframe, tomados de la página actual (sirve igual en Razor y en demo.html). */
    function assets() {
        var css = $('link[rel="stylesheet"]').map(function () { return this.href; }).get();
        var js = [];
        var jq = $('script[src*="jquery"]')[0];
        if (jq) js.push(jq.src);
        $.each(['survey-utils', 'survey-types', 'survey-logic', 'survey-renderer'], function (_, name) {
            var s = $('script[src*="' + name + '"]')[0];
            if (s) js.push(s.src);
        });
        return { css: css, js: js };
    }

    /** JSON seguro para incrustarlo dentro de <script>. */
    function safeJson(value) {
        return JSON.stringify(value).replace(/</g, '\\u003c').replace(new RegExp('\\u2028', 'g'), '\\u2028').replace(new RegExp('\\u2029', 'g'), '\\u2029');
    }

    function render() {
        var survey = Store.survey();
        var version = Store.version();
        if (!survey || !version) return;

        var a = assets();
        var html = '<!DOCTYPE html><html lang="es"><head><meta charset="utf-8">' +
            '<meta name="viewport" content="width=device-width, initial-scale=1">' +
            $.map(a.css, function (href) { return '<link rel="stylesheet" href="' + href + '">'; }).join('') +
            '</head><body class="sv-respondent-body"><div id="surveyRoot" class="container sv-respond-container"></div>' +
            '<script>window.__SURVEY__=' + safeJson({ name: survey.name, version: version }) + ';<\/script>' +
            $.map(a.js, function (src) { return '<script src="' + src + '"><\/script>'; }).join('') +
            '<script>' +
            'SurveyRenderer.mountSurvey($("#surveyRoot"), { name: __SURVEY__.name, version: __SURVEY__.version, preview: true,' +
            ' onRestart: function () { location.reload(); } });' +
            '<\/script></body></html>';

        $('#previewFrame').attr('srcdoc', html);
    }

    function setDevice(device) {
        $('#previewDevice').attr('data-device', device);
    }

    function show() {
        var el = document.querySelector('#tabPreviewBtn');
        bootstrap.Tab.getOrCreateInstance(el).show();
    }

    function bind() {
        $('#deviceSwitcher').on('change', 'input', function () { setDevice($(this).val()); });
        $('#btnRestartPreview').on('click', render);
        $('#tabPreviewBtn').on('shown.bs.tab', render);
        $('#btnPreview').on('click', show);
    }

    function init() {
        bind();
    }

    return { init: init, render: render, show: show };
})(jQuery);
