/* =========================================================================
 * survey-types.js
 * Catálogo de tipos de pregunta. Para agregar un tipo nuevo:
 *   1) Registrarlo aquí (capacidades + valores por defecto).
 *   2) Agregar su render en survey-renderer.js (render/readAnswer/validate).
 *   3) Si tiene configuración propia, agregar su sección en survey-editor.js.
 *   4) Agregar su resumen en survey-results.js.
 *   5) Insertarlo en la tabla QuestionType (Database/schema.sql).
 *
 * Capacidades:
 *   options     -> usa lista de opciones editable
 *   fixedOptions-> opciones predefinidas (solo se editan los textos)
 *   other       -> admite "Otro" con campo de texto
 *   randomize   -> admite orden aleatorio
 *   ruleKind    -> cómo se evalúan sus reglas condicionales:
 *                  option | number | text | answered
 * ========================================================================= */
var SurveyTypes = (function ($) {
    'use strict';

    var U = SurveyUtils;

    var types = {
        singleChoice: {
            label: 'Opción única', icon: 'bi-ui-radios', description: 'Una sola respuesta',
            options: true, other: true, randomize: true, ruleKind: 'option'
        },
        multipleChoice: {
            label: 'Selección múltiple', icon: 'bi-ui-checks', description: 'Varias respuestas',
            options: true, other: true, randomize: true, ruleKind: 'option', selections: true
        },
        dropdown: {
            label: 'Desplegable', icon: 'bi-menu-button-wide', description: 'Lista de opciones',
            options: true, randomize: true, ruleKind: 'option'
        },
        yesNo: {
            label: 'Sí / No', icon: 'bi-toggle-on', description: 'Respuesta binaria',
            options: true, fixedOptions: true, ruleKind: 'option'
        },
        text: {
            label: 'Texto libre', icon: 'bi-textarea-t', description: 'Respuesta abierta',
            ruleKind: 'text', chars: true
        },
        scale: {
            label: 'Escala', icon: 'bi-sliders', description: 'Valoración numérica (1 a 5, 1 a 10)',
            ruleKind: 'number'
        },
        number: {
            label: 'Número', icon: 'bi-123', description: 'Solo valores numéricos',
            ruleKind: 'number', range: true
        },
        date: {
            label: 'Fecha', icon: 'bi-calendar-event', description: 'Selector de fecha',
            ruleKind: 'answered'
        },
        matrix: {
            label: 'Matriz', icon: 'bi-grid-3x3', description: 'Filas evaluadas con la misma escala',
            ruleKind: 'answered'
        },
        dependent: {
            label: 'Multiselección dependiente', icon: 'bi-diagram-3', description: 'Listas encadenadas por niveles',
            ruleKind: 'option'
        }
    };

    /** Orden en que se muestran en la paleta y en el menú "Agregar pregunta". */
    var order = ['singleChoice', 'multipleChoice', 'dropdown', 'text', 'yesNo', 'scale', 'matrix', 'dependent', 'date', 'number'];

    function get(code) {
        return types[code] || types.singleChoice;
    }

    function list() {
        return $.map(order, function (code) { return $.extend({ code: code }, types[code]); });
    }

    function makeOptions(texts) {
        return $.map(texts, function (t) { return { id: U.newId('o'), text: t }; });
    }

    /** Configuración por defecto de cada tipo (se usa al crear o cambiar de tipo). */
    function applyDefaults(q, type) {
        q.type = type;
        q.validation = q.validation || {};
        q.rules = q.rules || [];

        if (type === 'yesNo') {
            q.options = makeOptions(['Sí', 'No']);
        } else if (types[type].options) {
            q.options = makeOptions(['Opción 1', 'Opción 2', 'Opción 3']);
        } else {
            q.options = [];
        }

        if (!types[type].other) q.allowOther = false;
        if (!types[type].randomize) q.randomizeOptions = false;
        if (type !== 'dropdown') delete q.parentQuestionId;   // cascada: solo desplegables
        if (type !== 'text') delete q.identifier;            // identificador: solo texto

        if (type === 'text') {
            q.validation = { textMode: 'long', minLength: null, maxLength: 500 };
        } else if (type === 'multipleChoice') {
            q.validation = { minSelections: null, maxSelections: null };
        } else if (type === 'number') {
            q.validation = { minValue: null, maxValue: null };
        } else if (type === 'scale') {
            q.validation = { scaleMin: 1, scaleMax: 5, scaleMinLabel: 'Nada satisfecho', scaleMaxLabel: 'Muy satisfecho' };
        } else {
            q.validation = {};
        }

        delete q.matrix;
        delete q.dependent;

        if (type === 'matrix') {
            q.matrix = {
                selectionType: 'single',
                rows: makeOptions(['Aspecto 1', 'Aspecto 2']),
                columns: makeOptions(['Malo', 'Regular', 'Bueno', 'Excelente'])
            };
        }

        if (type === 'dependent') {
            var parent = { id: U.newId('d'), parentId: null, level: 0, text: 'Opción A' };
            q.dependent = {
                multiple: true,
                levels: [{ id: U.newId('l'), name: 'Nivel 1' }, { id: U.newId('l'), name: 'Nivel 2' }],
                options: [parent, { id: U.newId('d'), parentId: parent.id, level: 1, text: 'Subopción A.1' }]
            };
        }
        return q;
    }

    /** Crea una pregunta nueva del tipo indicado. */
    function create(type) {
        var q = {
            id: U.newId('q'),
            order: 0,
            type: type,
            text: '',
            helpText: '',
            required: false,
            randomizeOptions: false,
            allowOther: false,
            otherLabel: 'Otro (especifica)',
            validation: {},
            options: [],
            rules: []
        };
        return applyDefaults(q, type);
    }

    /**
     * Cambia el tipo conservando lo posible (texto, obligatoria, opciones si
     * ambos tipos usan opciones). Las reglas se conservan solo si el tipo de
     * evaluación es compatible.
     */
    function changeType(q, newType) {
        var before = get(q.type);
        var after = get(newType);
        var keepOptions = before.options && after.options && !before.fixedOptions && !after.fixedOptions;
        var options = q.options;

        applyDefaults(q, newType);
        if (keepOptions) q.options = options;
        // Las reglas de opción apuntan a IDs de opción: si las opciones cambiaron, ya no son válidas.
        if (before.ruleKind !== after.ruleKind || (after.ruleKind === 'option' && !keepOptions)) q.rules = [];
        return q;
    }

    return {
        get: get,
        list: list,
        create: create,
        changeType: changeType
    };
})(jQuery);
