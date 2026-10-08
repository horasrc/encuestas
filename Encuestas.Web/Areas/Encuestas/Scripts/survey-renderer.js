/* =========================================================================
 * survey-renderer.js
 * Dibuja las preguntas tal como las ve el colaborador que responde.
 * Se reutiliza en:
 *   - Constructor: vista previa de la pregunta seleccionada.
 *   - Pestaña "Vista previa" (dentro de un iframe: desktop/tablet/móvil).
 *   - Página real de respuesta (Views/Encuestas/Responder.cshtml).
 *
 * API pública:
 *   renderQuestion(question, displayNumber, options) -> jQuery
 *   readAnswer($question, question)                 -> answer | null
 *   validate(question, answer)                      -> string | null
 *   mountSurvey($root, config)                      -> encuesta completa
 *
 * Formato de respuesta (igual al AnswerDto de C#):
 *   { optionIds: [], otherText: '', text: '', number: 0, date: 'yyyy-mm-dd',
 *     matrix: { rowId: [colId] } }
 * ========================================================================= */
var SurveyRenderer = (function ($) {
    'use strict';

    var U = SurveyUtils;
    var esc = U.escapeHtml;
    var OTHER = '__other__';
    var instance = 0;

    // ---------------------------------------------------------------------
    // Render por tipo
    // ---------------------------------------------------------------------

    function orderedOptions(q, opts) {
        var list = q.options || [];
        return q.randomizeOptions && !opts.noShuffle ? U.shuffle(list) : list;
    }

    function otherInput(q, name) {
        return '<input type="text" class="form-control form-control-sm sv-other-input mt-2 d-none" ' +
            'maxlength="200" placeholder="Especifica…" aria-label="Especifica otra opción" data-other-for="' + esc(name) + '">';
    }

    function renderChoice(q, name, opts, inputType) {
        var html = '<div class="sv-choice-list" role="' + (inputType === 'radio' ? 'radiogroup' : 'group') + '">';
        $.each(orderedOptions(q, opts), function (i, o) {
            var id = name + '_' + i;
            html += '<div class="form-check sv-choice">' +
                '<input class="form-check-input" type="' + inputType + '" name="' + esc(name) + '" id="' + esc(id) + '" value="' + esc(o.id) + '">' +
                '<label class="form-check-label" for="' + esc(id) + '">' + esc(o.text) + '</label></div>';
        });
        if (q.allowOther) {
            var oid = name + '_other';
            html += '<div class="form-check sv-choice sv-choice-other">' +
                '<input class="form-check-input" type="' + inputType + '" name="' + esc(name) + '" id="' + esc(oid) + '" value="' + OTHER + '">' +
                '<label class="form-check-label" for="' + esc(oid) + '">' + esc(q.otherLabel || 'Otro') + '</label>' +
                otherInput(q, name) + '</div>';
        }
        html += '</div>';

        var v = q.validation || {};
        if (inputType === 'checkbox' && (v.minSelections || v.maxSelections)) {
            html += '<div class="form-text">' + esc(selectionHint(v)) + '</div>';
        }
        return html;
    }

    function selectionHint(v) {
        if (v.minSelections && v.maxSelections) return 'Selecciona entre ' + v.minSelections + ' y ' + v.maxSelections + ' opciones.';
        if (v.maxSelections) return 'Selecciona hasta ' + v.maxSelections + ' opciones.';
        return 'Selecciona al menos ' + v.minSelections + ' opciones.';
    }

    function renderDropdown(q, name, opts) {
        var html = '<select class="form-select sv-dropdown" name="' + esc(name) + '" aria-label="' + esc(q.text) + '">' +
            '<option value="">Selecciona una opción…</option>';
        $.each(orderedOptions(q, opts), function (_, o) {
            html += '<option value="' + esc(o.id) + '">' + esc(o.text) + '</option>';
        });
        return html + '</select>';
    }

    function renderYesNo(q, name) {
        var html = '<div class="sv-yesno" role="radiogroup">';
        $.each(q.options || [], function (i, o) {
            var id = name + '_' + i;
            html += '<input type="radio" class="btn-check" name="' + esc(name) + '" id="' + esc(id) + '" value="' + esc(o.id) + '" autocomplete="off">' +
                '<label class="btn btn-outline-dark" for="' + esc(id) + '">' +
                '<i class="bi ' + (i === 0 ? 'bi-hand-thumbs-up' : 'bi-hand-thumbs-down') + ' me-1"></i>' + esc(o.text) + '</label>';
        });
        return html + '</div>';
    }

    function renderText(q, name) {
        var v = q.validation || {};
        var max = v.maxLength ? ' maxlength="' + Number(v.maxLength) + '"' : '';
        var field = v.textMode === 'short'
            ? '<input type="text" class="form-control sv-text" name="' + esc(name) + '"' + max + ' placeholder="Escribe tu respuesta">'
            : '<textarea class="form-control sv-text" name="' + esc(name) + '" rows="3"' + max + ' placeholder="Escribe tu respuesta"></textarea>';
        var hint = v.minLength ? 'Mínimo ' + v.minLength + ' caracteres' : '';
        return field +
            '<div class="d-flex justify-content-between form-text">' +
            '<span>' + esc(hint) + '</span>' +
            '<span class="sv-counter" aria-live="polite">0' + (v.maxLength ? ' / ' + Number(v.maxLength) : '') + '</span></div>';
    }

    function renderScale(q, name) {
        var v = q.validation || {};
        var min = Number(v.scaleMin || 1);
        var max = Number(v.scaleMax || 5);
        var html = '<div class="sv-scale" role="radiogroup">';
        for (var n = min; n <= max; n++) {
            var id = name + '_' + n;
            html += '<input type="radio" class="btn-check" name="' + esc(name) + '" id="' + esc(id) + '" value="' + n + '" autocomplete="off">' +
                '<label class="btn btn-outline-dark sv-scale-btn" for="' + esc(id) + '">' + n + '</label>';
        }
        html += '</div>';
        if (v.scaleMinLabel || v.scaleMaxLabel) {
            html += '<div class="sv-scale-labels"><span>' + esc(v.scaleMinLabel || '') + '</span><span>' + esc(v.scaleMaxLabel || '') + '</span></div>';
        }
        return html;
    }

    function renderNumber(q, name) {
        var v = q.validation || {};
        var attrs = (v.minValue !== null && v.minValue !== undefined ? ' min="' + Number(v.minValue) + '"' : '') +
            (v.maxValue !== null && v.maxValue !== undefined ? ' max="' + Number(v.maxValue) + '"' : '');
        var hint = '';
        if (attrs) {
            hint = (v.minValue !== null && v.minValue !== undefined && v.maxValue !== null && v.maxValue !== undefined)
                ? 'Entre ' + v.minValue + ' y ' + v.maxValue
                : (v.minValue !== null && v.minValue !== undefined ? 'Mínimo ' + v.minValue : 'Máximo ' + v.maxValue);
        }
        return '<input type="number" inputmode="decimal" step="any" class="form-control sv-number" name="' + esc(name) + '"' + attrs + ' placeholder="0">' +
            (hint ? '<div class="form-text">' + esc(hint) + '</div>' : '');
    }

    function renderDate(q, name) {
        return '<input type="date" class="form-control sv-date" name="' + esc(name) + '">';
    }

    function renderMatrix(q, name) {
        var m = q.matrix || { rows: [], columns: [], selectionType: 'single' };
        var type = m.selectionType === 'multiple' ? 'checkbox' : 'radio';
        var html = '<div class="table-responsive sv-matrix-wrap"><table class="table sv-matrix align-middle mb-0">' +
            '<thead><tr><th scope="col"><span class="visually-hidden">Aspecto</span></th>';
        $.each(m.columns, function (_, c) { html += '<th scope="col" class="text-center">' + esc(c.text) + '</th>'; });
        html += '</tr></thead><tbody>';
        $.each(m.rows, function (ri, r) {
            var rowName = name + '_r' + ri;
            html += '<tr data-row-id="' + esc(r.id) + '"><th scope="row">' + esc(r.text) + '</th>';
            $.each(m.columns, function (ci, c) {
                html += '<td class="text-center" data-label="' + esc(c.text) + '">' +
                    '<input class="form-check-input" type="' + type + '" name="' + esc(rowName) + '" value="' + esc(c.id) + '" ' +
                    'aria-label="' + esc(r.text + ': ' + c.text) + '"></td>';
            });
            html += '</tr>';
        });
        return html + '</tbody></table></div>';
    }

    function depChildren(q, parentId) {
        return $.grep(q.dependent.options, function (o) { return (o.parentId || null) === (parentId || null); });
    }

    /** Multiselección dependiente: checkboxes anidados (multiple) o selects encadenados (single). */
    function renderDependent(q, name) {
        var d = q.dependent || { levels: [], options: [] };
        var path = $.map(d.levels, function (l) { return esc(l.name); }).join(' <i class="bi bi-chevron-right"></i> ');
        var html = '<div class="sv-dep-path small text-muted mb-2"><i class="bi bi-diagram-3 me-1"></i>' + path + '</div>';

        if (d.multiple) {
            html += '<div class="sv-dep-tree row g-3">';
            $.each(depChildren(q, null), function (i, o) {
                html += '<div class="col-12 col-md-6 col-lg-4">' + depNode(q, name, o) + '</div>';
            });
            return html + '</div>';
        }

        html += '<div class="row g-2 sv-dep-selects">';
        $.each(d.levels, function (i, l) {
            html += '<div class="col-12 col-md"><label class="form-label small mb-1">' + esc(l.name) + '</label>' +
                '<select class="form-select sv-dep-select" data-level="' + i + '"' + (i > 0 ? ' disabled' : '') + '>' +
                '<option value="">Selecciona…</option>' +
                (i === 0 ? depOptionTags(depChildren(q, null)) : '') +
                '</select></div>';
        });
        return html + '</div>';
    }

    function depOptionTags(list) {
        return $.map(list, function (o) { return '<option value="' + esc(o.id) + '">' + esc(o.text) + '</option>'; }).join('');
    }

    function depNode(q, name, o) {
        var id = name + '_' + o.id;
        var kids = depChildren(q, o.id);
        var html = '<div class="sv-dep-node" data-dep-id="' + esc(o.id) + '">' +
            '<div class="form-check"><input class="form-check-input sv-dep-check" type="checkbox" id="' + esc(id) + '" value="' + esc(o.id) + '">' +
            '<label class="form-check-label' + (o.level === 0 ? ' fw-semibold' : '') + '" for="' + esc(id) + '">' + esc(o.text) + '</label></div>';
        if (kids.length) {
            html += '<div class="sv-dep-children d-none">';
            $.each(kids, function (_, k) { html += depNode(q, name, k); });
            html += '</div>';
        }
        return html + '</div>';
    }

    var renderers = {
        singleChoice: function (q, name, opts) { return renderChoice(q, name, opts, 'radio'); },
        multipleChoice: function (q, name, opts) { return renderChoice(q, name, opts, 'checkbox'); },
        dropdown: renderDropdown,
        yesNo: renderYesNo,
        text: renderText,
        scale: renderScale,
        number: renderNumber,
        date: renderDate,
        matrix: renderMatrix,
        dependent: renderDependent
    };

    /**
     * Pregunta completa (tarjeta). options:
     *   noShuffle: true  -> no aleatorizar (útil en el constructor)
     */
    function renderQuestion(q, displayNumber, options) {
        var opts = options || {};
        instance += 1;
        var name = 'sv' + instance + '_' + q.id;
        var type = SurveyTypes.get(q.type);
        var renderer = renderers[q.type] || renderers.text;

        var $q = $(
            '<section class="sv-q card" data-qid="' + esc(q.id) + '" data-type="' + esc(q.type) + '">' +
            '  <div class="card-body">' +
            '    <div class="sv-q-head">' +
            '      <span class="sv-q-index" aria-hidden="true">' + esc(displayNumber) + '</span>' +
            '      <div class="flex-grow-1">' +
            '        <h2 class="sv-q-title" id="' + esc(name) + '_title">' + (esc(q.text) || '<span class="text-muted fst-italic">Escribe el texto de la pregunta</span>') +
            (q.required ? ' <span class="sv-required" title="Obligatoria">*</span>' : '') + '</h2>' +
            (q.helpText ? '<p class="sv-q-help">' + esc(q.helpText) + '</p>' : '') +
            '      </div>' +
            '    </div>' +
            '    <div class="sv-q-input" role="group" aria-labelledby="' + esc(name) + '_title">' + renderer(q, name, opts) + '</div>' +
            '    <div class="sv-q-error d-none" role="alert"></div>' +
            '  </div>' +
            '</section>');

        $q.attr('aria-label', type.label);
        bindInteractions($q, q);
        return $q;
    }

    /** Comportamientos locales: contador, "Otro", máximo de selecciones, cascadas. */
    function bindInteractions($q, q) {
        var v = q.validation || {};

        // Contador de caracteres.
        $q.on('input', '.sv-text', function () {
            var len = $(this).val().length;
            var $c = $q.find('.sv-counter');
            $c.text(len + (v.maxLength ? ' / ' + v.maxLength : ''));
            $c.toggleClass('text-danger', !!(v.minLength && len > 0 && len < v.minLength));
        });

        // Campo "Otro".
        $q.on('change', 'input[value="' + OTHER + '"], .sv-choice input', function () {
            var $other = $q.find('input[value="' + OTHER + '"]');
            var on = $other.is(':checked');
            $q.find('.sv-other-input').toggleClass('d-none', !on);
            if (on && this.value === OTHER) $q.find('.sv-other-input').trigger('focus');
        });

        // Máximo de selecciones: deshabilita las casillas restantes.
        if (q.type === 'multipleChoice' && v.maxSelections) {
            $q.on('change', '.sv-choice input[type=checkbox]', function () {
                var $all = $q.find('.sv-choice input[type=checkbox]');
                var reached = $all.filter(':checked').length >= v.maxSelections;
                $all.not(':checked').prop('disabled', reached);
            });
        }

        // Dependiente (múltiple): al marcar se despliegan los hijos; al desmarcar se limpian.
        $q.on('change', '.sv-dep-check', function () {
            var $node = $(this).closest('.sv-dep-node');
            var $kids = $node.children('.sv-dep-children');
            if (this.checked) {
                $kids.removeClass('d-none');
            } else {
                $kids.addClass('d-none').find('.sv-dep-check').prop('checked', false);
                $kids.find('.sv-dep-children').addClass('d-none');
            }
        });

        // Dependiente (único): selects encadenados.
        $q.on('change', '.sv-dep-select', function () {
            var level = Number($(this).data('level'));
            var value = $(this).val();
            var $selects = $q.find('.sv-dep-select');
            $selects.each(function () {
                var l = Number($(this).data('level'));
                if (l <= level) return;
                var $s = $(this).html('<option value="">Selecciona…</option>');
                if (l === level + 1 && value) {
                    var kids = depChildren(q, value);
                    $s.append(depOptionTags(kids)).prop('disabled', !kids.length);
                } else {
                    $s.prop('disabled', true);
                }
            });
        });
    }

    // ---------------------------------------------------------------------
    // Lectura y validación
    // ---------------------------------------------------------------------

    function readAnswer($q, q) {
        var a = {};
        switch (q.type) {
            case 'singleChoice':
            case 'multipleChoice':
            case 'yesNo':
                a.optionIds = $q.find('.sv-q-input input:checked').not('.sv-other-input').map(function () { return this.value; }).get();
                if ($.inArray(OTHER, a.optionIds) >= 0) a.otherText = $.trim($q.find('.sv-other-input').val());
                break;
            case 'dropdown':
                var val = $q.find('select').val();
                a.optionIds = val ? [val] : [];
                break;
            case 'text':
                a.text = $q.find('.sv-text').val();
                break;
            case 'scale':
                var s = $q.find('.sv-scale input:checked').val();
                a.number = s === undefined ? null : Number(s);
                break;
            case 'number':
                var raw = $q.find('.sv-number').val();
                a.number = raw === '' ? null : Number(raw);
                // type=number devuelve '' si el texto no es numérico; validity.badInput lo distingue de "vacío".
                if (raw === '' && $q.find('.sv-number')[0].validity.badInput) a.rawInvalid = true;
                break;
            case 'date':
                a.date = $q.find('.sv-date').val() || null;
                break;
            case 'matrix':
                a.matrix = {};
                $q.find('tr[data-row-id]').each(function () {
                    var cols = $(this).find('input:checked').map(function () { return this.value; }).get();
                    if (cols.length) a.matrix[$(this).attr('data-row-id')] = cols;
                });
                break;
            case 'dependent':
                a.optionIds = $q.find('.sv-dep-check:checked').map(function () { return this.value; }).get();
                $q.find('.sv-dep-select').each(function () { if ($(this).val()) a.optionIds.push($(this).val()); });
                break;
        }
        return SurveyLogic.isEmpty(a) && !a.rawInvalid ? null : a;
    }

    /** Devuelve el mensaje de error o null si la respuesta es válida. */
    function validate(q, answer) {
        var v = q.validation || {};
        if (SurveyLogic.isEmpty(answer)) {
            if (answer && answer.rawInvalid) return 'Ingresa un número válido.';
            return q.required ? 'Esta pregunta es obligatoria.' : null;
        }

        if (answer.optionIds && $.inArray(OTHER, answer.optionIds) >= 0 && !answer.otherText) {
            return 'Especifica tu respuesta en «' + (q.otherLabel || 'Otro') + '».';
        }

        switch (q.type) {
            case 'multipleChoice':
                var count = answer.optionIds.length;
                if (v.minSelections && count < v.minSelections) return 'Selecciona al menos ' + v.minSelections + ' opciones.';
                if (v.maxSelections && count > v.maxSelections) return 'Selecciona como máximo ' + v.maxSelections + ' opciones.';
                break;
            case 'text':
                var len = $.trim(answer.text).length;
                if (v.minLength && len < v.minLength) return 'Escribe al menos ' + v.minLength + ' caracteres (llevas ' + len + ').';
                if (v.maxLength && len > v.maxLength) return 'Máximo ' + v.maxLength + ' caracteres.';
                break;
            case 'number':
                if (isNaN(answer.number)) return 'Ingresa un número válido.';
                if (v.minValue !== null && v.minValue !== undefined && answer.number < v.minValue) return 'El valor mínimo es ' + v.minValue + '.';
                if (v.maxValue !== null && v.maxValue !== undefined && answer.number > v.maxValue) return 'El valor máximo es ' + v.maxValue + '.';
                break;
            case 'matrix':
                if (q.required) {
                    var missing = $.grep(q.matrix.rows, function (r) { return !answer.matrix[r.id]; });
                    if (missing.length) return 'Responde todas las filas (faltan ' + missing.length + ').';
                }
                break;
            case 'dependent':
                if (q.required && !q.dependent.multiple && answer.optionIds.length < q.dependent.levels.length) {
                    return 'Completa todos los niveles.';
                }
                break;
        }
        return null;
    }

    function showError($q, message) {
        $q.toggleClass('sv-q-invalid', !!message);
        $q.find('.sv-q-error').toggleClass('d-none', !message).html(message ? '<i class="bi bi-exclamation-circle me-1"></i>' + esc(message) : '');
    }

    // ---------------------------------------------------------------------
    // Encuesta completa
    // ---------------------------------------------------------------------

    /**
     * Monta la encuesta completa con lógica condicional en vivo.
     * config: {
     *   name, version, preview (bool),
     *   onSubmit(answers) -> promesa jQuery (opcional),
     *   onRestart()      (opcional, solo vista previa)
     * }
     */
    function mountSurvey($root, config) {
        var version = config.version;
        var settings = version.settings || {};
        var questions = SurveyLogic.sorted(version.questions || []);
        var $cards = {};

        var $page = $(
            '<div class="sv-respond">' +
            (config.preview ? '<div class="sv-preview-banner"><i class="bi bi-eye me-1"></i>Vista previa · las respuestas no se guardan</div>' : '') +
            '<header class="sv-respond-header">' +
            '  <h1 class="sv-respond-title">' + esc(config.name) + '</h1>' +
            (settings.welcomeMessage ? '<p class="sv-respond-welcome">' + esc(settings.welcomeMessage) + '</p>' : '') +
            '  <div class="sv-respond-meta">' +
            (settings.anonymous ? '<span><i class="bi bi-incognito me-1"></i>Respuestas anónimas</span>' : '') +
            (settings.endDate ? '<span><i class="bi bi-calendar-check me-1"></i>Disponible hasta ' + esc(U.formatDate(settings.endDate)) + '</span>' : '') +
            '  </div>' +
            '  <div class="sv-progress"><div class="progress" role="progressbar" aria-label="Avance"><div class="progress-bar"></div></div>' +
            '  <span class="sv-progress-text small text-muted"></span></div>' +
            '</header>' +
            '<form class="sv-respond-form" novalidate>' +
            '  <div class="sv-respond-questions"></div>' +
            '  <div class="alert sv-end-notice d-none"><i class="bi bi-flag me-2"></i>Según tus respuestas, la encuesta termina aquí.</div>' +
            '  <div class="d-flex justify-content-end mt-3"><button type="submit" class="btn btn-accent btn-lg px-4">Enviar respuestas <i class="bi bi-send ms-1"></i></button></div>' +
            '</form>' +
            '<div class="sv-thanks d-none text-center">' +
            '  <div class="sv-thanks-icon"><i class="bi bi-check2-circle"></i></div>' +
            '  <h2 class="h4">' + esc(settings.thankYouMessage || '¡Gracias por tu respuesta!') + '</h2>' +
            '  <p class="text-muted">Tu opinión nos ayuda a mejorar.</p>' +
            (config.preview ? '<button type="button" class="btn btn-outline-dark sv-restart"><i class="bi bi-arrow-counterclockwise me-1"></i>Volver a empezar</button>' : '') +
            '</div>' +
            '</div>');

        var $list = $page.find('.sv-respond-questions');
        $.each(questions, function (i, q) {
            $cards[q.id] = renderQuestion(q, i + 1).appendTo($list);
        });

        function collect() {
            var answers = {};
            $.each(questions, function (_, q) {
                var a = readAnswer($cards[q.id], q);
                if (a) answers[q.id] = a;
            });
            return answers;
        }

        /** Aplica la lógica condicional y renumera las preguntas visibles. */
        function refresh() {
            var answers = collect();
            var state = SurveyLogic.computeVisibility(questions, answers);
            var n = 0, answered = 0;
            $.each(questions, function (_, q) {
                var show = state.visible[q.id] !== false;
                var $c = $cards[q.id];
                if (show !== !$c.hasClass('d-none')) {
                    $c.toggleClass('d-none', !show);
                    if (show) $c.addClass('sv-q-reveal'); else showError($c, null);
                }
                if (show) {
                    n += 1;
                    $c.find('.sv-q-index').first().text(n);
                    if (answers[q.id]) answered += 1;
                }
            });
            $page.find('.sv-end-notice').toggleClass('d-none', !state.endedAt);
            var pct = n ? Math.round(answered / n * 100) : 0;
            $page.find('.progress-bar').css('width', pct + '%');
            $page.find('.sv-progress-text').text(answered + ' de ' + n + ' preguntas respondidas');
            return { answers: answers, state: state };
        }

        $page.on('change input', '.sv-q-input', function () {
            var $q = $(this).closest('.sv-q');
            if ($q.hasClass('sv-q-invalid')) {
                var q = findQuestion($q.attr('data-qid'));
                showError($q, validate(q, readAnswer($q, q)));
            }
            refresh();
        });

        function findQuestion(id) {
            for (var i = 0; i < questions.length; i++) if (questions[i].id === id) return questions[i];
            return null;
        }

        $page.on('submit', 'form', function (e) {
            e.preventDefault();
            var result = refresh();
            var firstError = null;
            var payload = {};

            $.each(questions, function (_, q) {
                var $c = $cards[q.id];
                if (result.state.visible[q.id] === false) return; // las ocultas no se validan ni se envían
                var answer = result.answers[q.id];
                var error = validate(q, answer);
                showError($c, error);
                if (error && !firstError) firstError = $c;
                if (answer && !error) payload[q.id] = answer;
            });

            if (firstError) {
                firstError[0].scrollIntoView({ behavior: 'smooth', block: 'center' });
                return;
            }

            var $btn = $(this).find('[type=submit]').prop('disabled', true);
            var done = config.onSubmit ? config.onSubmit(payload) : $.Deferred().resolve().promise();
            $.when(done).done(function () {
                $page.find('form, .sv-respond-header .sv-progress').addClass('d-none');
                $page.find('.sv-thanks').removeClass('d-none');
                window.scrollTo(0, 0);
            }).always(function () { $btn.prop('disabled', false); });
        });

        $page.on('click', '.sv-restart', function () {
            if (config.onRestart) config.onRestart();
        });

        $root.empty().append($page);
        refresh();
        return { refresh: refresh, collect: collect };
    }

    return {
        OTHER: OTHER,
        renderQuestion: renderQuestion,
        readAnswer: readAnswer,
        validate: validate,
        showError: showError,
        mountSurvey: mountSurvey
    };
})(jQuery);
