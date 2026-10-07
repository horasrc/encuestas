/* =========================================================================
 * survey-editor.js
 * Panel "Editar pregunta" (columna central del constructor).
 *
 * Estrategia de render:
 *   - render() dibuja el panel completo. Se llama al cambiar de pregunta
 *     o en cambios estructurales (agregar/eliminar opción, cambiar tipo,
 *     agregar regla...).
 *   - Lo que el usuario escribe (texto, opciones, valores) actualiza el
 *     modelo SIN volver a dibujar, para no perder el foco del cursor.
 *
 * Atributos usados en el HTML generado:
 *   data-field="ruta"        campo de la pregunta (ej. "validation.maxLength")
 *   data-kind="number|bool"  cómo convertir el valor
 *   data-rerender            volver a dibujar tras el cambio
 *   data-list="ruta"         lista editable (options, matrix.rows, ...)
 *   data-act="accion"        botones de acción
 * ========================================================================= */
var SurveyEditor = (function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var Store = SurveyStore;

    /** Estado visual que se conserva entre renders. */
    var ui = {
        open: { validation: true, logic: true },
        depPath: {} // pregunta -> [id nivel 0, id nivel 1, ...] seleccionados en el editor de dependientes
    };

    // ---------------------------------------------------------------------
    // Render principal
    // ---------------------------------------------------------------------

    function render() {
        var $ed = $('#questionEditor');
        var q = Store.selected();
        var editable = Store.isEditable();

        if (!q) {
            $ed.html('<div class="card-body sv-empty"><i class="bi bi-hand-index"></i><p>Selecciona una pregunta de la lista o agrega una nueva.</p></div>');
            return;
        }

        var questions = Store.questions();
        var label = SurveyLogic.questionLabel(questions, q.id);
        var index = $.inArray(q, questions);
        var type = SurveyTypes.get(q.type);

        var html =
            '<div class="card-header sv-card-header">' +
            '  <span class="fw-semibold">Editar pregunta <span class="sv-chip sv-chip-dark ms-1">' + esc(label) + '</span></span>' +
            '  <div class="btn-group btn-group-sm sv-edit-only" role="group" aria-label="Acciones de la pregunta">' +
            '    <button type="button" class="btn btn-light border" data-act="up" title="Subir"' + (index <= 0 ? ' disabled' : '') + '><i class="bi bi-arrow-up"></i></button>' +
            '    <button type="button" class="btn btn-light border" data-act="down" title="Bajar"' + (index >= questions.length - 1 ? ' disabled' : '') + '><i class="bi bi-arrow-down"></i></button>' +
            '    <button type="button" class="btn btn-light border" data-act="duplicate" title="Duplicar"><i class="bi bi-copy"></i></button>' +
            '    <button type="button" class="btn btn-light border text-danger" data-act="delete" title="Eliminar"><i class="bi bi-trash"></i></button>' +
            '  </div>' +
            '</div>' +
            '<fieldset id="questionEditorFieldset"' + (editable ? '' : ' disabled') + '>' +
            '<div class="card-body">' +
            fieldType(q) +
            '<div class="mb-3">' +
            '  <label class="form-label" for="edText">Pregunta</label>' +
            '  <textarea id="edText" class="form-control" rows="2" maxlength="1000" data-field="text" placeholder="Ej. ¿Qué tan útil fue el material?">' + esc(q.text) + '</textarea>' +
            '</div>' +
            '<div class="mb-3">' +
            '  <label class="form-label" for="edHelp">Descripción <span class="text-muted fw-normal">(opcional)</span></label>' +
            '  <input id="edHelp" type="text" class="form-control form-control-sm" maxlength="1000" data-field="helpText" value="' + esc(q.helpText) + '" placeholder="Instrucción breve para quien responde">' +
            '</div>' +
            typeSection(q, type) +
            switches(q, type) +
            '<div class="accordion sv-accordion mt-3" id="edAccordion">' +
            validationSection(q, type) +
            logicSection(q, questions) +
            '</div>' +
            '</div></fieldset>';

        $ed.html(html);
        markValidation(q);
    }

    function fieldType(q) {
        var options = $.map(SurveyTypes.list(), function (t) {
            return '<option value="' + t.code + '"' + (t.code === q.type ? ' selected' : '') + '>' + esc(t.label) + '</option>';
        }).join('');
        return '<div class="mb-3">' +
            '<label class="form-label" for="edType">Tipo de pregunta</label>' +
            '<div class="input-group">' +
            '  <span class="input-group-text"><i class="bi ' + SurveyTypes.get(q.type).icon + '"></i></span>' +
            '  <select id="edType" class="form-select" data-act-change="type">' + options + '</select>' +
            '</div></div>';
    }

    function switches(q, type) {
        var html = '<div class="sv-switches">' + switchControl('edRequired', 'required', q.required, 'Obligatoria');
        if (type.randomize) html += switchControl('edRandom', 'randomizeOptions', q.randomizeOptions, 'Orden aleatorio de opciones');
        if (type.other) html += switchControl('edOther', 'allowOther', q.allowOther, 'Permitir «Otro»', true);
        html += '</div>';
        if (type.other && q.allowOther) {
            html += '<div class="mt-2"><label class="form-label small" for="edOtherLabel">Texto de la opción «Otro»</label>' +
                '<input id="edOtherLabel" type="text" class="form-control form-control-sm" maxlength="100" data-field="otherLabel" value="' + esc(q.otherLabel) + '"></div>';
        }
        return html;
    }

    function switchControl(id, field, checked, label, rerender) {
        return '<div class="form-check form-switch">' +
            '<input class="form-check-input" type="checkbox" role="switch" id="' + id + '" data-field="' + field + '" data-kind="bool"' +
            (rerender ? ' data-rerender' : '') + (checked ? ' checked' : '') + '>' +
            '<label class="form-check-label" for="' + id + '">' + esc(label) + '</label></div>';
    }

    // ---------------------------------------------------------------------
    // Sección específica por tipo
    // ---------------------------------------------------------------------

    function typeSection(q, type) {
        var body = '';
        switch (q.type) {
            case 'singleChoice':
            case 'multipleChoice':
            case 'dropdown':
            case 'yesNo':
                body = '<label class="form-label">Opciones de respuesta</label>' +
                    listEditor(q.options, 'options', { fixed: type.fixedOptions, placeholder: 'Texto de la opción', addLabel: 'Agregar opción' });
                break;
            case 'text':
                body = '<label class="form-label">Formato de respuesta</label>' +
                    radioGroup('textMode', q.validation.textMode || 'long', [
                        { value: 'short', label: 'Campo corto', icon: 'bi-input-cursor-text' },
                        { value: 'long', label: 'Párrafo (textarea)', icon: 'bi-text-paragraph' }
                    ]);
                break;
            case 'scale':
                body = scaleSection(q);
                break;
            case 'matrix':
                body = matrixSection(q);
                break;
            case 'dependent':
                body = dependentSection(q);
                break;
            case 'date':
                body = '<div class="sv-hint"><i class="bi bi-calendar-event"></i> Se mostrará un selector de fecha.</div>';
                break;
            case 'number':
                body = '<div class="sv-hint"><i class="bi bi-123"></i> Solo se aceptan valores numéricos. Define el rango en «Validaciones».</div>';
                break;
        }
        return '<div class="sv-type-section mb-3">' + body + '</div>';
    }

    /** Lista editable reutilizable: opciones, filas y columnas de matriz. */
    function listEditor(items, path, opts) {
        var html = '<div class="sv-opt-list" data-list="' + path + '">';
        $.each(items || [], function (i, item) {
            html += '<div class="sv-opt-row" data-sort-id="' + esc(item.id) + '">' +
                (opts.fixed ? '<span class="sv-opt-bullet"><i class="bi bi-dot"></i></span>' : '<span class="drag-handle sv-edit-only" title="Arrastrar"><i class="bi bi-grip-vertical"></i></span>') +
                '<input type="text" class="form-control form-control-sm" data-item-text maxlength="300" value="' + esc(item.text) + '" placeholder="' + esc(opts.placeholder) + '" aria-label="' + esc(opts.placeholder + ' ' + (i + 1)) + '">' +
                (opts.fixed ? '' : '<button type="button" class="btn btn-sm btn-icon sv-edit-only" data-act="del-item" title="Eliminar"' + (items.length <= 1 ? ' disabled' : '') + '><i class="bi bi-trash"></i></button>') +
                '</div>';
        });
        html += '</div>';
        if (!opts.fixed) {
            html += '<button type="button" class="btn btn-sm sv-link-btn sv-edit-only" data-act="add-item" data-list="' + path + '"><i class="bi bi-plus-lg"></i> ' + esc(opts.addLabel) + '</button>';
        }
        return html;
    }

    function radioGroup(name, value, items) {
        var html = '<div class="btn-group w-100" role="group">';
        $.each(items, function (i, it) {
            var id = 'ed_' + name + '_' + i;
            html += '<input type="radio" class="btn-check" name="ed_' + name + '" id="' + id + '" value="' + esc(it.value) + '" data-radio="' + name + '"' + (it.value === value ? ' checked' : '') + '>' +
                '<label class="btn btn-sm btn-outline-dark" for="' + id + '"><i class="bi ' + it.icon + ' me-1"></i>' + esc(it.label) + '</label>';
        });
        return html + '</div>';
    }

    function scaleSection(q) {
        var v = q.validation;
        var mins = '', maxs = '';
        for (var a = 0; a <= 1; a++) mins += '<option value="' + a + '"' + (Number(v.scaleMin) === a ? ' selected' : '') + '>' + a + '</option>';
        for (var b = 3; b <= 10; b++) maxs += '<option value="' + b + '"' + (Number(v.scaleMax) === b ? ' selected' : '') + '>' + b + '</option>';
        return '<label class="form-label">Escala</label>' +
            '<div class="d-flex flex-wrap align-items-center gap-2 mb-2">' +
            '  <select class="form-select form-select-sm w-auto" data-field="validation.scaleMin" data-kind="number" data-rerender aria-label="Mínimo">' + mins + '</select>' +
            '  <span class="small">a</span>' +
            '  <select class="form-select form-select-sm w-auto" data-field="validation.scaleMax" data-kind="number" data-rerender aria-label="Máximo">' + maxs + '</select>' +
            '  <span class="ms-auto small text-muted">Rápido:</span>' +
            '  <button type="button" class="btn btn-sm btn-light border" data-act="scale-preset" data-min="1" data-max="5">1 – 5</button>' +
            '  <button type="button" class="btn btn-sm btn-light border" data-act="scale-preset" data-min="1" data-max="10">1 – 10</button>' +
            '</div>' +
            '<div class="row g-2">' +
            '  <div class="col-6"><input type="text" class="form-control form-control-sm" data-field="validation.scaleMinLabel" value="' + esc(v.scaleMinLabel) + '" placeholder="Etiqueta del mínimo" aria-label="Etiqueta del mínimo"></div>' +
            '  <div class="col-6"><input type="text" class="form-control form-control-sm" data-field="validation.scaleMaxLabel" value="' + esc(v.scaleMaxLabel) + '" placeholder="Etiqueta del máximo" aria-label="Etiqueta del máximo"></div>' +
            '</div>';
    }

    function matrixSection(q) {
        var m = q.matrix;
        return '<label class="form-label">Tipo de selección por fila</label>' +
            radioGroup('matrixSelection', m.selectionType, [
                { value: 'single', label: 'Una respuesta por fila', icon: 'bi-record-circle' },
                { value: 'multiple', label: 'Varias por fila', icon: 'bi-check2-square' }
            ]) +
            '<div class="row g-3 mt-1">' +
            '  <div class="col-md-6"><label class="form-label">Filas <span class="text-muted fw-normal">(aspectos)</span></label>' +
            listEditor(m.rows, 'matrix.rows', { placeholder: 'Fila', addLabel: 'Agregar fila' }) + '</div>' +
            '  <div class="col-md-6"><label class="form-label">Columnas <span class="text-muted fw-normal">(escala)</span></label>' +
            listEditor(m.columns, 'matrix.columns', { placeholder: 'Columna', addLabel: 'Agregar columna' }) + '</div>' +
            '</div>';
    }

    // ---- Multiselección dependiente: columnas por nivel ----

    function depChildren(q, parentId, level) {
        return $.grep(q.dependent.options, function (o) { return o.level === level && (o.parentId || null) === (parentId || null); });
    }

    function depPath(q) {
        var path = ui.depPath[q.id] || [];
        // Limpia selecciones que ya no existen.
        var clean = [];
        for (var i = 0; i < path.length; i++) {
            var exists = $.grep(q.dependent.options, function (o) { return o.id === path[i] && o.level === i && (o.parentId || null) === (i ? clean[i - 1] : null); }).length;
            if (!exists) break;
            clean.push(path[i]);
        }
        // Por defecto selecciona el primer elemento de cada nivel para que se vea la cadena completa.
        for (var l = clean.length; l < q.dependent.levels.length - 1; l++) {
            var first = depChildren(q, l ? clean[l - 1] : null, l)[0];
            if (!first) break;
            clean.push(first.id);
        }
        ui.depPath[q.id] = clean;
        return clean;
    }

    function dependentSection(q) {
        var d = q.dependent;
        var path = depPath(q);
        var html = '<div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-2">' +
            '<label class="form-label mb-0">Listas encadenadas</label>' +
            switchControl('edDepMultiple', 'dependent.multiple', d.multiple, 'Permitir selección múltiple') +
            '</div>' +
            '<p class="small text-muted mb-2">Las opciones de cada nivel dependen de la selección del nivel anterior. Usa <i class="bi bi-chevron-right"></i> para ver y editar los hijos de una opción.</p>' +
            '<div class="sv-dep-editor">';

        $.each(d.levels, function (level, lv) {
            var parentId = level ? path[level - 1] : null;
            var items = level && !parentId ? [] : depChildren(q, parentId, level);
            var parentText = '';
            if (level && parentId) {
                var parent = $.grep(d.options, function (o) { return o.id === parentId; })[0];
                parentText = parent ? parent.text : '';
            }

            html += '<div class="sv-dep-col">' +
                '<div class="sv-dep-col-head">' +
                '  <input type="text" class="form-control form-control-sm fw-semibold" data-level-name="' + level + '" value="' + esc(lv.name) + '" aria-label="Nombre del nivel ' + (level + 1) + '">' +
                (level === d.levels.length - 1 && level > 0 ? '<button type="button" class="btn btn-sm btn-icon sv-edit-only" data-act="dep-del-level" title="Eliminar nivel"><i class="bi bi-x-lg"></i></button>' : '') +
                '</div>' +
                (level ? '<div class="sv-dep-col-parent">' + (parentText ? 'De: <strong>' + esc(parentText) + '</strong>' : 'Selecciona una opción del nivel anterior') + '</div>' : '<div class="sv-dep-col-parent">Primer nivel</div>') +
                '<div class="sv-dep-col-list" data-dep-level="' + level + '">';

            $.each(items, function (_, o) {
                var kids = depChildren(q, o.id, level + 1).length;
                var active = path[level] === o.id;
                var hasNext = level < d.levels.length - 1;
                html += '<div class="sv-dep-item' + (active && hasNext ? ' active' : '') + '" data-sort-id="' + esc(o.id) + '">' +
                    '<span class="drag-handle sv-edit-only"><i class="bi bi-grip-vertical"></i></span>' +
                    '<input type="text" class="form-control form-control-sm" data-dep-text value="' + esc(o.text) + '" aria-label="Opción">' +
                    (hasNext ? '<button type="button" class="btn btn-sm btn-icon" data-act="dep-select" title="Ver hijos"><span class="sv-dep-kids">' + kids + '</span><i class="bi bi-chevron-right"></i></button>' : '') +
                    '<button type="button" class="btn btn-sm btn-icon sv-edit-only" data-act="dep-del" title="Eliminar"><i class="bi bi-trash"></i></button>' +
                    '</div>';
            });

            html += '</div>';
            if (!level || parentId) {
                html += '<div class="input-group input-group-sm mt-2 sv-edit-only">' +
                    '<input type="text" class="form-control" data-dep-new="' + level + '" placeholder="Nueva opción" aria-label="Nueva opción de ' + esc(lv.name) + '">' +
                    '<button type="button" class="btn btn-outline-dark" data-act="dep-add" data-level="' + level + '" title="Agregar"><i class="bi bi-plus-lg"></i></button>' +
                    '</div>';
            }
            html += '</div>';
        });

        if (d.levels.length < 5) {
            html += '<button type="button" class="sv-dep-add-level sv-edit-only" data-act="dep-add-level"><i class="bi bi-plus-lg"></i><span>Agregar nivel</span></button>';
        }
        return html + '</div>';
    }

    // ---------------------------------------------------------------------
    // Validaciones (acordeón)
    // ---------------------------------------------------------------------

    function numberField(id, field, value, label, attrs) {
        return '<label class="form-label small" for="' + id + '">' + esc(label) + '</label>' +
            '<input type="number" class="form-control form-control-sm" id="' + id + '" data-field="' + field + '" data-kind="number" value="' +
            (value === null || value === undefined ? '' : esc(value)) + '" ' + (attrs || '') + ' placeholder="Sin límite">';
    }

    function validationSection(q, type) {
        var v = q.validation || {};
        var body = '';
        var summary = [];

        if (q.type === 'text') {
            body = '<div class="row g-2">' +
                '<div class="col-6">' + numberField('edMinLen', 'validation.minLength', v.minLength, 'Mínimo de caracteres', 'min="0"') + '</div>' +
                '<div class="col-6">' + numberField('edMaxLen', 'validation.maxLength', v.maxLength, 'Máximo de caracteres', 'min="1"') + '</div>' +
                '</div><div class="form-text">Quien responde verá un contador de caracteres.</div>';
            if (v.minLength) summary.push('mín. ' + v.minLength);
            if (v.maxLength) summary.push('máx. ' + v.maxLength);
        } else if (q.type === 'multipleChoice') {
            body = '<div class="row g-2">' +
                '<div class="col-6">' + numberField('edMinSel', 'validation.minSelections', v.minSelections, 'Mínimo de selecciones', 'min="0"') + '</div>' +
                '<div class="col-6">' + numberField('edMaxSel', 'validation.maxSelections', v.maxSelections, 'Máximo de selecciones', 'min="1"') + '</div>' +
                '</div>';
            if (v.minSelections) summary.push('mín. ' + v.minSelections);
            if (v.maxSelections) summary.push('máx. ' + v.maxSelections);
        } else if (q.type === 'number') {
            body = '<div class="row g-2">' +
                '<div class="col-6">' + numberField('edMinVal', 'validation.minValue', v.minValue, 'Valor mínimo', 'step="any"') + '</div>' +
                '<div class="col-6">' + numberField('edMaxVal', 'validation.maxValue', v.maxValue, 'Valor máximo', 'step="any"') + '</div>' +
                '</div>';
            if (v.minValue !== null && v.minValue !== undefined) summary.push('≥ ' + v.minValue);
            if (v.maxValue !== null && v.maxValue !== undefined) summary.push('≤ ' + v.maxValue);
        } else {
            return '';
        }

        return accordionItem('validation', '<i class="bi bi-shield-check me-2"></i>Validaciones',
            summary.length ? '<span class="badge sv-badge-soft ms-2" id="edValidationSummary">' + esc(summary.join(' · ')) + '</span>' : '',
            '<div class="sv-validation">' + body + '<div class="invalid-feedback d-block d-none" id="edValidationError"></div></div>');
    }

    /** Marca en rojo combinaciones inválidas (mínimo > máximo). */
    function markValidation(q) {
        var v = q.validation || {};
        var pairs = [['minLength', 'maxLength', 'caracteres'], ['minSelections', 'maxSelections', 'selecciones'], ['minValue', 'maxValue', 'valor']];
        var error = '';
        $.each(pairs, function (_, p) {
            var bad = v[p[0]] !== null && v[p[0]] !== undefined && v[p[1]] !== null && v[p[1]] !== undefined && Number(v[p[0]]) > Number(v[p[1]]);
            $('#questionEditor [data-field="validation.' + p[0] + '"], #questionEditor [data-field="validation.' + p[1] + '"]').toggleClass('is-invalid', bad);
            if (bad) error = 'El mínimo de ' + p[2] + ' no puede ser mayor que el máximo.';
        });
        if (q.type === 'multipleChoice' && v.maxSelections && v.maxSelections > q.options.length + (q.allowOther ? 1 : 0)) {
            error = 'El máximo de selecciones es mayor que el número de opciones.';
        }
        $('#edValidationError').toggleClass('d-none', !error).text(error);
    }

    // ---------------------------------------------------------------------
    // Lógica condicional (acordeón)
    // ---------------------------------------------------------------------

    function logicSection(q, questions) {
        var rules = q.rules || [];
        var incoming = SurveyLogic.incomingRules(q.id, questions);
        var body = '';

        if (incoming.length) {
            body += '<div class="sv-incoming"><div class="small fw-semibold mb-1"><i class="bi bi-box-arrow-in-down-right me-1"></i>Esta pregunta depende de:</div>';
            $.each(incoming, function (_, x) {
                body += '<div class="small">' + esc(SurveyLogic.questionLabel(questions, x.source.id)) + ' · ' + SurveyLogic.describeRule(x.rule, x.source, questions) + '</div>';
            });
            body += '</div>';
        }

        if (!rules.length) {
            body += '<p class="small text-muted mb-2">Define qué pasa según la respuesta. Ejemplo: <em>Si la respuesta es «Sí» → Mostrar P5</em>.</p>';
        }

        $.each(rules, function (i, r) { body += ruleRow(q, r, questions); });

        body += '<button type="button" class="btn btn-sm sv-link-btn sv-edit-only" data-act="rule-add"><i class="bi bi-plus-lg"></i> Agregar regla</button>';

        return accordionItem('logic', '<i class="bi bi-signpost-split me-2"></i>Lógica condicional',
            rules.length ? '<span class="badge sv-badge-logic ms-2">' + rules.length + (rules.length === 1 ? ' regla' : ' reglas') + '</span>' : '',
            body);
    }

    function ruleRow(q, r, questions) {
        var ops = SurveyLogic.operatorsFor(q);
        var opHtml = $.map(ops, function (o) {
            return '<option value="' + o.value + '"' + (o.value === r.operator ? ' selected' : '') + '>' + esc(o.label) + '</option>';
        }).join('');

        var actHtml = $.map(SurveyLogic.ACTIONS, function (a) {
            return '<option value="' + a.value + '"' + (a.value === r.action ? ' selected' : '') + '>' + esc(a.label) + '</option>';
        }).join('');

        var candidates = SurveyLogic.targetCandidates(q, questions);
        var targetHtml = '<option value="">Elige pregunta…</option>' + $.map(candidates, function (c) {
            var label = SurveyLogic.questionLabel(questions, c.id) + ' · ' + (c.text || 'Sin texto');
            if (label.length > 60) label = label.substring(0, 57) + '…';
            return '<option value="' + esc(c.id) + '"' + (c.id === r.targetQuestionId ? ' selected' : '') + '>' + esc(label) + '</option>';
        }).join('');

        return '<div class="sv-rule" data-rule-id="' + esc(r.id) + '">' +
            '<div class="sv-rule-row">' +
            '  <span class="sv-rule-kw">SI</span><span class="small text-muted">la respuesta</span>' +
            '  <select class="form-select form-select-sm sv-rule-op" data-rule-field="operator" aria-label="Condición">' + opHtml + '</select>' +
            ruleValueControl(q, r) +
            '</div>' +
            '<div class="sv-rule-row">' +
            '  <span class="sv-rule-kw sv-rule-kw-then">ENTONCES</span>' +
            '  <select class="form-select form-select-sm sv-rule-act" data-rule-field="action" aria-label="Acción">' + actHtml + '</select>' +
            (r.action === 'end' ? '' : '<select class="form-select form-select-sm sv-rule-target" data-rule-field="targetQuestionId" aria-label="Pregunta destino">' + targetHtml + '</select>') +
            '</div>' +
            (r.action !== 'end' && !candidates.length ? '<div class="small text-danger mt-1">No hay preguntas posteriores. Agrega preguntas después de esta o elige «Finalizar encuesta».</div>' : '') +
            '<div class="sv-rule-summary">' + SurveyLogic.describeRule(r, q, questions) + '</div>' +
            '<button type="button" class="btn btn-sm btn-icon sv-rule-del sv-edit-only" data-act="rule-del" title="Eliminar regla"><i class="bi bi-x-lg"></i></button>' +
            '</div>';
    }

    function ruleValueControl(q, r) {
        if (r.operator === 'answered') return '';
        var kind = SurveyLogic.ruleKind(q);
        if (kind === 'option') {
            var opts = SurveyLogic.comparableOptions(q);
            var html = '<select class="form-select form-select-sm sv-rule-val" data-rule-field="value" aria-label="Valor">';
            var group = null;
            $.each(opts, function (_, o) {
                if (o.group !== undefined && o.group !== group) {
                    if (group !== null) html += '</optgroup>';
                    group = o.group;
                    html += '<optgroup label="' + esc(group) + '">';
                }
                html += '<option value="' + esc(o.id) + '"' + (o.id === r.value ? ' selected' : '') + '>' + esc(o.text || '(sin texto)') + '</option>';
            });
            if (group !== null) html += '</optgroup>';
            return html + '</select>';
        }
        return '<input type="' + (kind === 'number' ? 'number' : 'text') + '" class="form-control form-control-sm sv-rule-val" data-rule-field="value" value="' +
            esc(r.value) + '" placeholder="Valor" aria-label="Valor">';
    }

    function accordionItem(key, title, badge, body) {
        var open = ui.open[key];
        var id = 'edAcc_' + key;
        return '<div class="accordion-item">' +
            '<h2 class="accordion-header" id="' + id + 'H">' +
            '<button class="accordion-button' + (open ? '' : ' collapsed') + '" type="button" data-bs-toggle="collapse" data-bs-target="#' + id + '" aria-expanded="' + open + '" aria-controls="' + id + '">' +
            title + badge + '</button></h2>' +
            '<div id="' + id + '" class="accordion-collapse collapse' + (open ? ' show' : '') + '" aria-labelledby="' + id + 'H" data-acc-key="' + key + '">' +
            '<div class="accordion-body">' + body + '</div></div></div>';
    }

    // ---------------------------------------------------------------------
    // Operaciones
    // ---------------------------------------------------------------------

    function current() { return Store.selected(); }

    function update(mutator, rerender) {
        var q = current();
        if (!q || !Store.isEditable()) return;
        Store.update(q.id, mutator);
        if (rerender) render(); else markValidation(q);
    }

    function focusLater(selector) {
        setTimeout(function () { $('#questionEditor').find(selector).last().trigger('focus'); }, 0);
    }

    /** Quita reglas que comparan contra opciones eliminadas. */
    function dropRulesForValues(q, ids) {
        var before = q.rules.length;
        q.rules = $.grep(q.rules, function (r) { return $.inArray(r.value, ids) < 0; });
        if (q.rules.length < before) U.toast('Se eliminaron ' + (before - q.rules.length) + ' regla(s) que usaban esa opción.', 'info');
    }

    function newRule(q) {
        var questions = Store.questions();
        var candidates = SurveyLogic.targetCandidates(q, questions);
        var ops = SurveyLogic.operatorsFor(q);
        var values = SurveyLogic.comparableOptions(q);
        var kind = SurveyLogic.ruleKind(q);
        return {
            id: U.newId('r'),
            operator: ops[0].value,
            value: kind === 'option' && values.length ? values[0].id : '',
            action: candidates.length ? 'show' : 'end',
            targetQuestionId: candidates.length ? candidates[0].id : null
        };
    }

    function reorderList(list, ids) {
        var byId = {};
        $.each(list, function (_, it) { byId[it.id] = it; });
        return $.map(ids, function (id) { return byId[id]; });
    }

    // ---------------------------------------------------------------------
    // Eventos (delegados: el HTML del panel se regenera)
    // ---------------------------------------------------------------------

    function bind() {
        var $ed = $('#questionEditor');

        // Acciones del encabezado del panel.
        $ed.on('click', '.card-header [data-act]', function () {
            var q = current();
            if (q) SurveyBuilder.questionAction($(this).attr('data-act'), q.id);
        });

        // Cambio de tipo.
        $ed.on('change', '[data-act-change="type"]', function () {
            var q = current();
            var newType = $(this).val();
            var lossy = (q.rules || []).length || q.type === 'matrix' || q.type === 'dependent';
            var apply = function () { update(function (x) { SurveyTypes.changeType(x, newType); }, true); };
            if (!lossy) { apply(); return; }
            U.confirmDialog({
                title: 'Cambiar tipo de pregunta',
                message: '<p>Al cambiar a <strong>' + esc(SurveyTypes.get(newType).label) + '</strong> se perderá la configuración específica del tipo actual y, si no son compatibles, sus reglas condicionales.</p>',
                confirmText: 'Cambiar tipo'
            }).done(apply).fail(function () { render(); });
        });

        // Campos simples de la pregunta.
        $ed.on('input change', '[data-field]', function (e) {
            var $el = $(this);
            var isText = this.tagName === 'TEXTAREA' || (this.tagName === 'INPUT' && this.type !== 'checkbox' && this.type !== 'radio');
            if (e.type === 'change' && isText && $el.attr('data-kind') !== 'number') return; // ya se procesó en "input"
            if (e.type === 'input' && !isText) return;

            var kind = $el.attr('data-kind');
            var value = kind === 'bool' ? this.checked : kind === 'number' ? U.toNumberOrNull($el.val()) : $el.val();
            update(function (q) { U.setPath(q, $el.attr('data-field'), value); }, $el.is('[data-rerender]'));
        });

        // Botones de radio (formato de texto, selección de matriz).
        $ed.on('change', '[data-radio]', function () {
            var name = $(this).attr('data-radio');
            var value = $(this).val();
            update(function (q) {
                if (name === 'textMode') q.validation.textMode = value;
                if (name === 'matrixSelection') q.matrix.selectionType = value;
            });
        });

        // Presets de escala.
        $ed.on('click', '[data-act="scale-preset"]', function () {
            var min = Number($(this).attr('data-min')), max = Number($(this).attr('data-max'));
            update(function (q) { q.validation.scaleMin = min; q.validation.scaleMax = max; }, true);
        });

        // ---- Listas editables (opciones / filas / columnas) ----
        $ed.on('input', '[data-item-text]', function () {
            var path = $(this).closest('[data-list]').attr('data-list');
            var id = $(this).closest('.sv-opt-row').attr('data-sort-id');
            var text = $(this).val();
            update(function (q) {
                $.each(U.getPath(q, path), function (_, it) { if (it.id === id) it.text = text; });
            });
        });

        $ed.on('keydown', '[data-item-text]', function (e) {
            if (e.key !== 'Enter') return;
            e.preventDefault();
            var $row = $(this).closest('.sv-opt-row');
            var path = $(this).closest('[data-list]').attr('data-list');
            if (path === 'options' && SurveyTypes.get(current().type).fixedOptions) return; // Sí/No: opciones fijas
            var after = $row.attr('data-sort-id');
            update(function (q) {
                var list = U.getPath(q, path);
                var idx = 0;
                $.each(list, function (i, it) { if (it.id === after) idx = i + 1; });
                list.splice(idx, 0, { id: U.newId('o'), text: '' });
            }, true);
            var $list = $('#questionEditor [data-list="' + path + '"]');
            var pos = $list.find('.sv-opt-row[data-sort-id="' + after + '"]').index() + 1;
            setTimeout(function () { $list.find('.sv-opt-row').eq(pos).find('input').trigger('focus'); }, 0);
        });

        $ed.on('click', '[data-act="add-item"]', function () {
            var path = $(this).attr('data-list');
            update(function (q) { U.getPath(q, path).push({ id: U.newId('o'), text: '' }); }, true);
            focusLater('[data-list="' + path + '"] [data-item-text]');
        });

        $ed.on('click', '[data-act="del-item"]', function () {
            var path = $(this).closest('[data-list]').attr('data-list');
            var id = $(this).closest('.sv-opt-row').attr('data-sort-id');
            update(function (q) {
                U.setPath(q, path, $.grep(U.getPath(q, path), function (it) { return it.id !== id; }));
                dropRulesForValues(q, [id]);
            }, true);
        });

        // ---- Multiselección dependiente ----
        $ed.on('input', '[data-dep-text]', function () {
            var id = $(this).closest('.sv-dep-item').attr('data-sort-id');
            var text = $(this).val();
            update(function (q) { $.each(q.dependent.options, function (_, o) { if (o.id === id) o.text = text; }); });
        });

        $ed.on('input', '[data-level-name]', function () {
            var level = Number($(this).attr('data-level-name'));
            var name = $(this).val();
            update(function (q) { q.dependent.levels[level].name = name; });
        });

        $ed.on('click', '[data-act="dep-select"]', function () {
            var q = current();
            var id = $(this).closest('.sv-dep-item').attr('data-sort-id');
            var opt = $.grep(q.dependent.options, function (o) { return o.id === id; })[0];
            var path = depPath(q).slice(0, opt.level);
            path.push(id);
            ui.depPath[q.id] = path;
            render();
        });

        var addDep = function (level, $input) {
            var text = $.trim($input.val());
            if (!text) { $input.trigger('focus'); return; }
            var q = current();
            var parentId = level ? depPath(q)[level - 1] : null;
            update(function (x) { x.dependent.options.push({ id: U.newId('d'), parentId: parentId, level: level, text: text }); }, true);
            focusLater('[data-dep-new="' + level + '"]');
        };
        $ed.on('click', '[data-act="dep-add"]', function () {
            var level = Number($(this).attr('data-level'));
            addDep(level, $(this).siblings('[data-dep-new]'));
        });
        $ed.on('keydown', '[data-dep-new]', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); addDep(Number($(this).attr('data-dep-new')), $(this)); }
        });

        $ed.on('click', '[data-act="dep-del"]', function () {
            var id = $(this).closest('.sv-dep-item').attr('data-sort-id');
            var q = current();
            var removed = [id];
            // Recorre descendientes.
            for (var i = 0; i < removed.length; i++) {
                $.each(q.dependent.options, function (_, o) { if (o.parentId === removed[i]) removed.push(o.id); });
            }
            var doRemove = function () {
                update(function (x) {
                    x.dependent.options = $.grep(x.dependent.options, function (o) { return $.inArray(o.id, removed) < 0; });
                    dropRulesForValues(x, removed);
                }, true);
            };
            if (removed.length > 1) {
                U.confirmDialog({ title: 'Eliminar opción', message: '<p>Se eliminarán también ' + (removed.length - 1) + ' opción(es) de niveles inferiores.</p>', confirmText: 'Eliminar', danger: true }).done(doRemove);
            } else {
                doRemove();
            }
        });

        $ed.on('click', '[data-act="dep-add-level"]', function () {
            update(function (q) { q.dependent.levels.push({ id: U.newId('l'), name: 'Nivel ' + (q.dependent.levels.length + 1) }); }, true);
            focusLater('[data-level-name]');
        });

        $ed.on('click', '[data-act="dep-del-level"]', function () {
            var q = current();
            var last = q.dependent.levels.length - 1;
            var ids = $.map($.grep(q.dependent.options, function (o) { return o.level === last; }), function (o) { return o.id; });
            var doRemove = function () {
                update(function (x) {
                    x.dependent.levels.pop();
                    x.dependent.options = $.grep(x.dependent.options, function (o) { return o.level < last; });
                    dropRulesForValues(x, ids);
                }, true);
            };
            if (ids.length) {
                U.confirmDialog({ title: 'Eliminar nivel', message: '<p>Se eliminarán ' + ids.length + ' opciones de este nivel.</p>', confirmText: 'Eliminar', danger: true }).done(doRemove);
            } else {
                doRemove();
            }
        });

        // ---- Reglas condicionales ----
        $ed.on('click', '[data-act="rule-add"]', function () {
            update(function (q) { q.rules.push(newRule(q)); }, true);
        });

        $ed.on('click', '[data-act="rule-del"]', function () {
            var id = $(this).closest('.sv-rule').attr('data-rule-id');
            update(function (q) { q.rules = $.grep(q.rules, function (r) { return r.id !== id; }); }, true);
        });

        $ed.on('change input', '[data-rule-field]', function (e) {
            var $el = $(this);
            var isTyped = this.tagName === 'INPUT';
            if (isTyped && e.type === 'change') return;
            if (!isTyped && e.type === 'input') return;

            var field = $el.attr('data-rule-field');
            var id = $el.closest('.sv-rule').attr('data-rule-id');
            var value = $el.val();
            var q = current();

            update(function (x) {
                $.each(x.rules, function (_, r) {
                    if (r.id !== id) return;
                    r[field] = value || (field === 'targetQuestionId' ? null : '');
                    if (field === 'operator' && value === 'answered') r.value = '';
                    if (field === 'action' && value !== 'end' && !r.targetQuestionId) {
                        var c = SurveyLogic.targetCandidates(x, Store.questions())[0];
                        r.targetQuestionId = c ? c.id : null;
                    }
                    if (field === 'action' && value === 'end') r.targetQuestionId = null;
                });
            }, !isTyped);

            if (isTyped) {
                var rule = $.grep(q.rules, function (r) { return r.id === id; })[0];
                $el.closest('.sv-rule').find('.sv-rule-summary').html(SurveyLogic.describeRule(rule, q, Store.questions()));
            }
        });

        // Recuerda qué secciones del acordeón quedaron abiertas.
        $ed.on('shown.bs.collapse hidden.bs.collapse', '[data-acc-key]', function (e) {
            ui.open[$(this).attr('data-acc-key')] = e.type === 'shown';
        });

        // Arrastrar opciones, filas, columnas y opciones dependientes.
        U.sortable($ed, '.sv-opt-row, .sv-dep-item', function (ids, parent) {
            var $parent = $(parent);
            if ($parent.is('[data-list]')) {
                var path = $parent.attr('data-list');
                update(function (q) { U.setPath(q, path, reorderList(U.getPath(q, path), ids)); });
            } else if ($parent.is('[data-dep-level]')) {
                update(function (q) {
                    var siblings = reorderList(q.dependent.options, ids);
                    q.dependent.options = $.grep(q.dependent.options, function (o) { return $.inArray(o.id, ids) < 0; }).concat(siblings);
                });
            }
        });

        Store.on('selection:changed', render);
        Store.on('questions:changed', render);
        Store.on('survey:loaded', render);
    }

    function init() {
        bind();
    }

    return { init: init, render: render };
})(jQuery);
