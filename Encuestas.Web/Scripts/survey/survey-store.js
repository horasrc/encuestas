/* =========================================================================
 * survey-store.js
 * Estado en memoria del constructor + operaciones sobre preguntas.
 * Los módulos de UI leen de aquí y se suscriben a eventos:
 *
 *   survey:loaded      se cargó/recargó la encuesta (o se cambió de versión)
 *   questions:changed  se agregó, eliminó, duplicó o reordenó una pregunta
 *   question:updated   cambió el contenido de una pregunta (data = pregunta)
 *   selection:changed  cambió la pregunta seleccionada
 *   dirty:changed      hay / no hay cambios sin guardar
 * ========================================================================= */
var SurveyStore = (function ($) {
    'use strict';

    var U = SurveyUtils;
    var bus = $({});
    var state = {
        survey: null,
        versionId: null,
        selectedId: null,
        dirty: false,
        catalogs: null
    };

    // ---------------------------------------------------------------------
    // Eventos
    // ---------------------------------------------------------------------
    function on(evt, fn) { bus.on(evt, function (e, data) { fn(data); }); }
    function emit(evt, data) { bus.trigger(evt, [data]); }

    // ---------------------------------------------------------------------
    // Encuesta / versión
    // ---------------------------------------------------------------------

    /** Carga la encuesta y elige versión: la indicada, el borrador, la publicada o la última. */
    function setSurvey(survey, preferredVersionId) {
        state.survey = survey;
        var pick = null;
        var byStatus = function (s) { return $.grep(survey.versions, function (v) { return v.status === s; })[0]; };
        if (preferredVersionId) pick = $.grep(survey.versions, function (v) { return v.id === preferredVersionId; })[0];
        pick = pick || byStatus('Draft') || byStatus('Published') || survey.versions[survey.versions.length - 1];
        var sameVersion = pick && pick.id === state.versionId;
        var previousSelection = state.selectedId;
        state.versionId = pick ? pick.id : null;
        state.selectedId = null;
        // Tras guardar se conserva la pregunta seleccionada; al cambiar de versión se selecciona la primera.
        if (sameVersion && question(previousSelection)) {
            state.selectedId = previousSelection;
        } else {
            var first = questions()[0];
            if (first) state.selectedId = first.id;
        }
        setDirty(false);
        emit('survey:loaded');
    }

    function survey() { return state.survey; }

    function version() {
        if (!state.survey) return null;
        return $.grep(state.survey.versions, function (v) { return v.id === state.versionId; })[0] || null;
    }

    function versions() {
        return state.survey ? state.survey.versions.slice().sort(function (a, b) { return b.number - a.number; }) : [];
    }

    function draftVersion() {
        return $.grep(versions(), function (v) { return v.status === 'Draft'; })[0] || null;
    }

    /**
     * Regla de versionado (igual que SurveyService.IsEditable):
     * se edita directo solo el borrador o una publicada sin respuestas.
     */
    function isEditable(v) {
        v = v || version();
        return !!v && (v.status === 'Draft' || (v.status === 'Published' && !v.responseCount));
    }

    function setName(name) {
        state.survey.name = name;
        setDirty(true);
    }

    function setDirty(value) {
        if (state.dirty === value) return;
        state.dirty = value;
        emit('dirty:changed', value);
    }

    // ---------------------------------------------------------------------
    // Preguntas
    // ---------------------------------------------------------------------

    function questions() {
        var v = version();
        return v ? SurveyLogic.sorted(v.questions) : [];
    }

    function question(id) {
        var v = version();
        if (!v) return null;
        return $.grep(v.questions, function (q) { return q.id === id; })[0] || null;
    }

    function selected() { return question(state.selectedId); }

    function select(id) {
        if (state.selectedId === id) return;
        state.selectedId = id;
        emit('selection:changed', id);
    }

    function renumber(list) {
        $.each(list, function (i, q) { q.order = i + 1; });
    }

    function changed(selectId) {
        renumber(questions());
        setDirty(true);
        if (selectId !== undefined) state.selectedId = selectId;
        emit('questions:changed');
        emit('selection:changed', state.selectedId);
    }

    /** Inserta una pregunta nueva después de afterId (o al final). */
    function add(type, afterId) {
        var list = questions();
        var q = SurveyTypes.create(type);
        var index = list.length;
        if (afterId) {
            for (var i = 0; i < list.length; i++) if (list[i].id === afterId) index = i + 1;
        }
        list.splice(index, 0, q);
        renumber(list);
        version().questions.push(q);
        changed(q.id);
        return q;
    }

    /** Duplica con IDs nuevos (las reglas propias se conservan apuntando a los mismos destinos). */
    function duplicate(id) {
        var src = question(id);
        if (!src) return null;
        var copy = U.clone(src);
        var map = {};
        var fresh = function (o, prefix) { var n = U.newId(prefix); map[o.id] = n; o.id = n; };

        fresh(copy, 'q');
        copy.text = src.text ? src.text + ' (copia)' : '';
        $.each(copy.options || [], function (_, o) { fresh(o, 'o'); });
        if (copy.matrix) {
            $.each(copy.matrix.rows, function (_, o) { fresh(o, 'o'); });
            $.each(copy.matrix.columns, function (_, o) { fresh(o, 'o'); });
        }
        if (copy.dependent) {
            $.each(copy.dependent.levels, function (_, l) { fresh(l, 'l'); });
            $.each(copy.dependent.options, function (_, o) { fresh(o, 'd'); });
            $.each(copy.dependent.options, function (_, o) { if (o.parentId) o.parentId = map[o.parentId]; });
        }
        $.each(copy.rules || [], function (_, r) {
            r.id = U.newId('r');
            if (map[r.value]) r.value = map[r.value];
        });

        var list = questions();
        var index = $.inArray(src, list);
        list.splice(index + 1, 0, copy);
        renumber(list);
        version().questions.push(copy);
        changed(copy.id);
        return copy;
    }

    /** Elimina la pregunta y las reglas de otras preguntas que la apuntaban. */
    function remove(id) {
        var list = questions();
        var index = -1;
        for (var i = 0; i < list.length; i++) if (list[i].id === id) index = i;
        if (index < 0) return;

        var v = version();
        v.questions = $.grep(v.questions, function (q) { return q.id !== id; });
        $.each(v.questions, function (_, q) {
            q.rules = $.grep(q.rules || [], function (r) { return r.targetQuestionId !== id; });
        });

        var remaining = questions();
        var next = remaining[Math.min(index, remaining.length - 1)];
        changed(next ? next.id : null);
    }

    function move(id, delta) {
        var list = questions();
        var i = -1;
        for (var k = 0; k < list.length; k++) if (list[k].id === id) i = k;
        var j = i + delta;
        if (i < 0 || j < 0 || j >= list.length) return;
        var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
        renumber(list);
        changed();
    }

    /** Reordena según el arreglo de IDs (drag & drop). */
    function reorder(ids) {
        $.each(ids, function (i, id) {
            var q = question(id);
            if (q) q.order = i + 1;
        });
        changed();
    }

    /** Aplica un cambio a la pregunta y notifica. */
    function update(id, mutator, options) {
        var q = question(id);
        if (!q) return;
        mutator(q);
        setDirty(true);
        emit('question:updated', $.extend({ question: q }, options || {}));
    }

    /** Problemas de lógica tras reordenar (reglas que apuntan hacia atrás). */
    function logicWarnings() {
        return SurveyLogic.validate(questions());
    }

    // ---------------------------------------------------------------------
    // Configuración (publicación y segmentación)
    // ---------------------------------------------------------------------

    function updateSettings(mutator) {
        var v = version();
        v.settings = v.settings || {};
        v.segmentation = v.segmentation || { mode: 'all', roles: [], segments: [], regions: [], branches: [], users: [] };
        mutator(v.settings, v.segmentation);
        setDirty(true);
        emit('settings:changed');
    }

    return {
        on: on,
        emit: emit,
        state: state,
        setSurvey: setSurvey,
        survey: survey,
        version: version,
        versions: versions,
        draftVersion: draftVersion,
        isEditable: isEditable,
        setName: setName,
        setDirty: setDirty,
        questions: questions,
        question: question,
        selected: selected,
        select: select,
        add: add,
        duplicate: duplicate,
        remove: remove,
        move: move,
        reorder: reorder,
        update: update,
        logicWarnings: logicWarnings,
        updateSettings: updateSettings
    };
})(jQuery);
