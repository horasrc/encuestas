/* =========================================================================
 * survey-logic.js
 * Motor de lógica condicional. Es puro (no toca el DOM) para poder:
 *   - usarse en el constructor (resúmenes legibles de las reglas),
 *   - usarse en la vista previa y en la página de respuesta,
 *   - portarse a C# para validar en el servidor.
 *
 * Regla:  SI respuesta(origen) [operator] [value]  ENTONCES [action] [target]
 *
 * Semántica de las acciones:
 *   show -> la pregunta destino está OCULTA por defecto y se muestra solo si
 *           alguna regla "show" que la apunta se cumple.
 *   hide -> la pregunta destino se oculta si la regla se cumple.
 *   goto -> se omiten las preguntas entre el origen y el destino.
 *   end  -> se omiten todas las preguntas posteriores y se finaliza.
 * Las preguntas ocultas no disparan reglas (sus respuestas se ignoran).
 * ========================================================================= */
var SurveyLogic = (function ($) {
    'use strict';

    var OPERATORS = {
        option: [
            { value: 'equals', label: 'es' },
            { value: 'notEquals', label: 'no es' }
        ],
        number: [
            { value: 'equals', label: 'es igual a' },
            { value: 'gt', label: 'es mayor que' },
            { value: 'lt', label: 'es menor que' }
        ],
        text: [
            { value: 'contains', label: 'contiene' },
            { value: 'answered', label: 'fue respondida' }
        ],
        answered: [
            { value: 'answered', label: 'fue respondida' }
        ]
    };

    var ACTIONS = [
        { value: 'show', label: 'Mostrar pregunta', icon: 'bi-eye' },
        { value: 'hide', label: 'Ocultar pregunta', icon: 'bi-eye-slash' },
        { value: 'goto', label: 'Ir a pregunta', icon: 'bi-arrow-return-right' },
        { value: 'end', label: 'Finalizar encuesta', icon: 'bi-flag' }
    ];

    function sorted(questions) {
        return questions.slice().sort(function (a, b) { return a.order - b.order; });
    }

    function ruleKind(question) {
        return SurveyTypes.get(question.type).ruleKind;
    }

    function operatorsFor(question) {
        return OPERATORS[ruleKind(question)] || OPERATORS.answered;
    }

    function action(value) {
        for (var i = 0; i < ACTIONS.length; i++) if (ACTIONS[i].value === value) return ACTIONS[i];
        return ACTIONS[0];
    }

    /** Opciones contra las que se puede comparar (incluye las de listas dependientes). */
    function comparableOptions(question) {
        if (question.type === 'dependent' && question.dependent) {
            var levels = question.dependent.levels;
            return $.map(question.dependent.options, function (o) {
                var level = levels[o.level] ? levels[o.level].name : 'Nivel ' + (o.level + 1);
                return { id: o.id, text: o.text, group: level };
            });
        }
        return question.options || [];
    }

    /** ¿La respuesta está vacía? */
    function isEmpty(answer) {
        if (!answer) return true;
        if (answer.optionIds && answer.optionIds.length) return false;
        if (answer.text && $.trim(answer.text).length) return false;
        if (answer.number !== undefined && answer.number !== null && answer.number !== '' && !isNaN(answer.number)) return false;
        if (answer.date) return false;
        if (answer.matrix) {
            for (var k in answer.matrix) {
                if (answer.matrix.hasOwnProperty(k) && answer.matrix[k] && answer.matrix[k].length) return false;
            }
        }
        return true;
    }

    /** Evalúa una regla contra la respuesta de su pregunta origen. */
    function evaluateRule(rule, question, answer) {
        if (isEmpty(answer)) return false; // sin respuesta no se dispara ninguna regla
        var kind = ruleKind(question);
        var ids = answer.optionIds || [];
        var num = Number(answer.number);
        var cmp = Number(rule.value);

        switch (rule.operator) {
            case 'equals':
                return kind === 'number' ? num === cmp : $.inArray(rule.value, ids) >= 0;
            case 'notEquals':
                return kind === 'number' ? num !== cmp : $.inArray(rule.value, ids) < 0;
            case 'gt':
                return num > cmp;
            case 'lt':
                return num < cmp;
            case 'contains':
                return String(answer.text || '').toLowerCase().indexOf(String(rule.value || '').toLowerCase()) >= 0;
            case 'answered':
                return true;
            default:
                return false;
        }
    }

    /** IDs de preguntas que tienen al menos una regla "show" apuntándolas (ocultas por defecto). */
    function showTargets(questions) {
        var set = {};
        $.each(questions, function (_, q) {
            $.each(q.rules || [], function (_, r) {
                if (r.action === 'show' && r.targetQuestionId) set[r.targetQuestionId] = true;
            });
        });
        return set;
    }

    /**
     * Calcula qué preguntas se muestran con las respuestas actuales.
     * @param questions  preguntas de la versión
     * @param answers    { questionId: answer }
     * @returns {{ visible: Object, endedAt: (string|null) }}
     */
    function computeVisibility(questions, answers) {
        var list = sorted(questions);
        var hiddenByDefault = showTargets(list);
        var visible = {};
        var skipUntil = null;
        var endedAt = null;

        $.each(list, function (_, q) { visible[q.id] = !hiddenByDefault[q.id]; });

        $.each(list, function (_, q) {
            if (endedAt) { visible[q.id] = false; return; }
            if (skipUntil) {
                if (q.id === skipUntil) skipUntil = null;
                else { visible[q.id] = false; return; }
            }
            if (!visible[q.id]) return;

            var answer = answers ? answers[q.id] : null;
            $.each(q.rules || [], function (_, rule) {
                if (!evaluateRule(rule, q, answer)) return;
                if (rule.action === 'show') visible[rule.targetQuestionId] = true;
                else if (rule.action === 'hide') visible[rule.targetQuestionId] = false;
                else if (rule.action === 'goto') skipUntil = rule.targetQuestionId;
                else if (rule.action === 'end') endedAt = q.id;
            });
        });

        return { visible: visible, endedAt: endedAt };
    }

    /** Número visible ("P3") de una pregunta según el orden actual. */
    function questionLabel(questions, id) {
        var list = sorted(questions);
        for (var i = 0; i < list.length; i++) if (list[i].id === id) return 'P' + (i + 1);
        return '¿?';
    }

    function valueLabel(rule, question) {
        if (rule.operator === 'answered') return '';
        if (ruleKind(question) === 'option') {
            var opts = comparableOptions(question);
            for (var i = 0; i < opts.length; i++) if (opts[i].id === rule.value) return '«' + opts[i].text + '»';
            return '«(opción eliminada)»';
        }
        return '«' + (rule.value === undefined || rule.value === null || rule.value === '' ? '…' : rule.value) + '»';
    }

    function operatorLabel(rule, question) {
        var ops = operatorsFor(question);
        for (var i = 0; i < ops.length; i++) if (ops[i].value === rule.operator) return ops[i].label;
        return rule.operator;
    }

    /**
     * Descripción legible de una regla. Devuelve HTML seguro.
     * Ej.: "Si la respuesta es «Sí» → Mostrar P5"
     */
    function describeRule(rule, question, questions) {
        var esc = SurveyUtils.escapeHtml;
        var act = action(rule.action);
        var target = rule.action === 'end' ? '' :
            ' <span class="sv-chip">' + esc(questionLabel(questions, rule.targetQuestionId)) + '</span>';
        return 'Si la respuesta <strong>' + esc(operatorLabel(rule, question)) + '</strong> ' +
            esc(valueLabel(rule, question)) +
            ' <i class="bi bi-arrow-right"></i> <span class="sv-rule-action sv-rule-' + esc(rule.action) + '">' +
            '<i class="bi ' + act.icon + '"></i> ' + esc(act.label.replace(' pregunta', '')) + '</span>' + target;
    }

    /** Reglas de otras preguntas que afectan a la pregunta indicada. */
    function incomingRules(questionId, questions) {
        var result = [];
        $.each(sorted(questions), function (_, q) {
            $.each(q.rules || [], function (_, r) {
                if (r.targetQuestionId === questionId) result.push({ source: q, rule: r });
            });
        });
        return result;
    }

    /** Preguntas válidas como destino: solo posteriores al origen (evita ciclos). */
    function targetCandidates(question, questions) {
        return $.grep(sorted(questions), function (q) { return q.order > question.order; });
    }

    /** Tipos que pueden ser "padre" de un desplegable en cascada. */
    var CASCADE_PARENT_TYPES = ['singleChoice', 'multipleChoice', 'dropdown', 'yesNo'];

    /** ¿Es un desplegable cuyas opciones dependen de otra pregunta? */
    function isCascade(question) {
        return question.type === 'dropdown' && !!question.parentQuestionId;
    }

    /** Preguntas anteriores que pueden ser padre de un desplegable en cascada. */
    function cascadeParentCandidates(question, questions) {
        return $.grep(sorted(questions), function (q) {
            return q.order < question.order && $.inArray(q.type, CASCADE_PARENT_TYPES) >= 0;
        });
    }

    /**
     * Opciones disponibles de un desplegable en cascada según la respuesta de la pregunta
     * padre (las opciones sin padre siempre están). Null si no es en cascada.
     * Igual que SurveyLogic.CascadeOptions en SurveyLogic.cs.
     */
    function cascadeOptions(question, parentAnswer) {
        if (!isCascade(question)) return null;
        var chosen = parentAnswer && parentAnswer.optionIds ? parentAnswer.optionIds : [];
        return $.grep(question.options || [], function (o) { return !o.parentId || $.inArray(o.parentId, chosen) >= 0; });
    }

    /** Pregunta marcada como identificador (ej. número de cliente) o null. */
    function identifierQuestion(questions) {
        return $.grep(sorted(questions), function (q) { return q.identifier && q.type === 'text'; })[0] || null;
    }

    /** Respuesta como texto legible (CSV de resultados, notas de la cita de Outlook). */
    function answerText(q, a) {
        if (!a) return '';
        var optionText = function (id) {
            if (id === '__other__') return (q.otherLabel || 'Otro') + ': ' + (a.otherText || '');
            var list = q.type === 'dependent' ? q.dependent.options : q.options;
            var o = $.grep(list || [], function (x) { return x.id === id; })[0];
            return o ? o.text : id;
        };
        switch (q.type) {
            case 'text': return a.text;
            case 'scale':
            case 'number': return String(a.number);
            case 'date': return a.date + (a.time ? ' ' + a.time : '');
            case 'matrix':
                return $.map(q.matrix.rows, function (r) {
                    var cols = (a.matrix || {})[r.id];
                    if (!cols) return null;
                    return r.text + ': ' + $.map(cols, function (c) {
                        var col = $.grep(q.matrix.columns, function (x) { return x.id === c; })[0];
                        return col ? col.text : c;
                    }).join('/');
                }).join(' | ');
            default:
                return $.map(a.optionIds || [], optionText).join('; ');
        }
    }

    /** Problemas de lógica para mostrar antes de publicar. */
    function validate(questions) {
        var errors = [];
        var byId = {};
        var list = sorted(questions);
        $.each(questions, function (_, q) { byId[q.id] = q; });
        $.each(list, function (i, q) {
            var label = 'P' + (i + 1);
            $.each(q.rules || [], function (_, r) {
                if (r.action !== 'end') {
                    var t = byId[r.targetQuestionId];
                    if (!t) errors.push(label + ': una regla apunta a una pregunta que no existe.');
                    else if (t.order <= q.order) errors.push(label + ': una regla apunta a una pregunta anterior (' + questionLabel(questions, t.id) + ').');
                }
                if (r.operator !== 'answered' && (r.value === undefined || r.value === null || r.value === ''))
                    errors.push(label + ': una regla no tiene valor de comparación.');
            });
            if (isCascade(q)) {
                var p = byId[q.parentQuestionId];
                if (!p) errors.push(label + ': depende de una pregunta que ya no existe.');
                else if (p.order > q.order) errors.push(label + ': depende de ' + questionLabel(questions, p.id) + ', que está después; muévela antes.');
                else {
                    var ids = $.map(p.options || [], function (o) { return o.id; });
                    var orphan = $.grep(q.options || [], function (o) { return o.parentId && $.inArray(o.parentId, ids) < 0; });
                    if (orphan.length) errors.push(label + ': una opción depende de una opción que ya no existe en ' + questionLabel(questions, p.id) + '.');
                }
            }
        });
        if ($.grep(list, function (q) { return q.identifier && q.type === 'text'; }).length > 1) {
            errors.push('Solo puede haber una pregunta marcada como identificador.');
        }
        return errors;
    }

    return {
        ACTIONS: ACTIONS,
        operatorsFor: operatorsFor,
        ruleKind: ruleKind,
        comparableOptions: comparableOptions,
        isEmpty: isEmpty,
        evaluateRule: evaluateRule,
        computeVisibility: computeVisibility,
        questionLabel: questionLabel,
        describeRule: describeRule,
        incomingRules: incomingRules,
        targetCandidates: targetCandidates,
        isCascade: isCascade,
        cascadeParentCandidates: cascadeParentCandidates,
        cascadeOptions: cascadeOptions,
        identifierQuestion: identifierQuestion,
        answerText: answerText,
        validate: validate,
        sorted: sorted
    };
})(jQuery);
