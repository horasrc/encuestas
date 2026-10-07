/* =========================================================================
 * mock-data.js
 * Datos de demostración. Solo se usan cuando SURVEY_CONFIG.useMock = true.
 * Al conectar el backend este archivo puede eliminarse de la vista.
 *
 * Contiene:
 *   - buildSurvey(): encuesta "Experiencia con campañas comerciales"
 *       v1 Publicada (con 128 respuestas) y v2 Borrador.
 *     Ejemplos incluidos: opción única, selección múltiple con "Otro",
 *     texto libre con límite, pregunta condicional, multiselección
 *     dependiente (Banca > Producto > Recurso) y matriz.
 *   - catalogs: roles, segmentos, regiones, sucursales y usuarios.
 *   - generateResponses(): respuestas simuladas (deterministas).
 * ========================================================================= */
var SurveyMock = (function ($) {
    'use strict';

    // ---------------------------------------------------------------------
    // Helpers para construir los datos de forma compacta
    // ---------------------------------------------------------------------

    function opts(prefix, texts) {
        return $.map(texts, function (t, i) { return { id: prefix + 'o' + (i + 1), text: t }; });
    }

    /** Convierte un árbol [texto, [hijos]] en la lista plana de DependentOption. */
    function depOptions(prefix, tree) {
        var list = [];
        var n = 0;
        function walk(nodes, parentId, level) {
            $.each(nodes, function (_, node) {
                n += 1;
                var id = prefix + 'd' + n;
                list.push({ id: id, parentId: parentId, level: level, text: node[0] });
                if (node[1]) walk(node[1], id, level + 1);
            });
        }
        walk(tree, null, 0);
        return list;
    }

    function question(id, order, type, text, extra) {
        return $.extend({
            id: id, order: order, type: type, text: text, helpText: '',
            required: false, randomizeOptions: false, allowOther: false, otherLabel: 'Otro (especifica)',
            validation: {}, options: [], rules: []
        }, extra);
    }

    // ---------------------------------------------------------------------
    // Encuesta de ejemplo
    // ---------------------------------------------------------------------

    function buildVersion1() {
        var p = 'v1';
        var questions = [
            // 1. OPCIÓN ÚNICA
            question(p + 'q1', 1, 'singleChoice', '¿Qué tan útil fue el material de la campaña?', {
                required: true,
                options: opts(p + 'q1', ['Muy útil', 'Útil', 'Neutral', 'Poco útil', 'Nada útil'])
            }),
            // 2. SELECCIÓN MÚLTIPLE + OTRO (mínimo 1, máximo 4)
            question(p + 'q2', 2, 'multipleChoice', '¿Qué materiales utilizaste en esta campaña?', {
                required: true,
                helpText: 'Puedes elegir varias opciones.',
                allowOther: true,
                options: opts(p + 'q2', ['Guía comercial', 'Argumentos de venta', 'Presentación', 'Videos', 'Correos electrónicos']),
                validation: { minSelections: 1, maxSelections: 4 }
            }),
            // 3. TEXTO LIBRE CON LÍMITE (10 a 500 caracteres)
            question(p + 'q3', 3, 'text', '¿Qué mejorarías en esta campaña?', {
                validation: { textMode: 'long', minLength: 10, maxLength: 500 }
            }),
            // 4. PREGUNTA CONDICIONAL
            question(p + 'q4', 4, 'singleChoice', '¿Actualmente utilizas Productivity Suite?', {
                required: true,
                options: opts(p + 'q4', ['Sí', 'No', 'No aplica']),
                rules: [
                    { id: p + 'r1', operator: 'equals', value: p + 'q4o1', action: 'show', targetQuestionId: p + 'q5' },
                    { id: p + 'r2', operator: 'equals', value: p + 'q4o2', action: 'show', targetQuestionId: p + 'q6' },
                    { id: p + 'r3', operator: 'equals', value: p + 'q4o3', action: 'end', targetQuestionId: null }
                ]
            }),
            // 5. MULTISELECCIÓN DEPENDIENTE (Banca > Producto > Recurso)
            question(p + 'q5', 5, 'dependent', '¿Qué recursos de Productivity Suite consultas?', {
                helpText: 'Marca la banca, después el producto y por último el recurso.',
                required: true,
                dependent: {
                    multiple: true,
                    levels: [
                        { id: p + 'l1', name: 'Banca' },
                        { id: p + 'l2', name: 'Producto' },
                        { id: p + 'l3', name: 'Recurso' }
                    ],
                    options: depOptions(p + 'q5', [
                        ['Banca Personal', [
                            ['Tarjeta de crédito', [['Guía comercial'], ['Argumentos de venta'], ['Manejo de objeciones'], ['Presentación']]],
                            ['Crédito sin garantía', [['Guía comercial'], ['Simulador']]],
                            ['Portabilidad', [['Argumentos de venta'], ['Manejo de objeciones']]],
                            ['Nómina', [['Guía comercial'], ['Presentación']]]
                        ]],
                        ['Premier', [
                            ['Inversiones', [['Guía comercial'], ['Presentación']]],
                            ['Seguros', [['Argumentos de venta'], ['Manejo de objeciones']]]
                        ]],
                        ['RBB Digital', [
                            ['Banca móvil', [['Guía comercial'], ['Tutorial en video']]],
                            ['Onboarding digital', [['Presentación']]]
                        ]]
                    ])
                }
            }),
            // 6. DESPLEGABLE (solo si respondió "No" en P4)
            question(p + 'q6', 6, 'dropdown', '¿Cuál es el principal motivo por el que no la utilizas?', {
                required: true,
                options: opts(p + 'q6', ['No conozco la herramienta', 'No tengo acceso', 'Prefiero otros recursos', 'No tengo tiempo'])
            }),
            // 7. MATRIZ
            question(p + 'q7', 7, 'matrix', 'Evalúa los siguientes aspectos', {
                required: true,
                helpText: 'Selecciona una opción por fila.',
                matrix: {
                    selectionType: 'single',
                    rows: opts(p + 'q7r', ['Facilidad de uso', 'Rapidez', 'Soporte', 'Claridad del material']),
                    columns: opts(p + 'q7c', ['Muy insatisfecho', 'Insatisfecho', 'Neutral', 'Satisfecho', 'Muy satisfecho'])
                }
            }),
            // 8. ESCALA 1 a 10
            question(p + 'q8', 8, 'scale', '¿Qué tan probable es que recomiendes esta campaña a un compañero?', {
                validation: { scaleMin: 1, scaleMax: 10, scaleMinLabel: 'Nada probable', scaleMaxLabel: 'Muy probable' }
            }),
            // 9. SÍ / NO
            question(p + 'q9', 9, 'yesNo', '¿Te gustaría recibir capacitación adicional sobre la campaña?', {
                options: opts(p + 'q9', ['Sí', 'No'])
            })
        ];

        return {
            id: 'v1',
            number: 1,
            status: 'Published',
            responseCount: 128,
            createdAt: '2026-08-20T10:00:00',
            publishedAt: '2026-09-01T09:00:00',
            settings: {
                startDate: '2026-09-01',
                endDate: '2026-10-31',
                allowMultipleResponses: false,
                anonymous: true,
                requireAuth: true,
                welcomeMessage: 'Queremos conocer tu experiencia con el material de la campaña. Te tomará menos de 5 minutos.',
                thankYouMessage: '¡Gracias por tu respuesta!'
            },
            segmentation: {
                mode: 'segmented',
                roles: ['ejecutivos', 'gerentes'],
                segments: ['bp', 'premier'],
                regions: [],
                branches: [],
                users: []
            },
            questions: questions
        };
    }

    /** v2 (borrador): copia de v1 con IDs nuevos + 2 preguntas nuevas (fecha y número). */
    function buildVersion2(v1) {
        var v2 = SurveyUtils.cloneVersion(v1, 2);
        v2.id = 'v2';
        v2.createdAt = '2026-10-02T16:30:00';
        v2.settings.startDate = '2026-11-03';
        v2.settings.endDate = '2026-11-30';

        v2.questions.push(question('v2q10', 10, 'date', '¿Cuándo fue tu última capacitación sobre la campaña?', {}));
        v2.questions.push(question('v2q11', 11, 'number', '¿Cuántos clientes contactaste con esta campaña la última semana?', {
            helpText: 'Escribe solo el número.',
            validation: { minValue: 0, maxValue: 200 }
        }));
        return v2;
    }

    function buildSurvey() {
        var v1 = buildVersion1();
        return {
            id: 's1',
            name: 'Experiencia con campañas comerciales',
            description: 'Percepción de la fuerza comercial sobre el material de la campaña.',
            versions: [v1, buildVersion2(v1)]
        };
    }

    // ---------------------------------------------------------------------
    // Catálogos organizacionales (segmentación)
    // ---------------------------------------------------------------------

    function branch(id, name, regionId, segmentId, ej, ge, sub, as) {
        return {
            id: id, name: name, regionId: regionId, segmentId: segmentId,
            headcount: { ejecutivos: ej, gerentes: ge, subdirectores: sub, asesores: as }
        };
    }

    var catalogs = {
        roles: [
            { id: 'ejecutivos', name: 'Ejecutivos' },
            { id: 'gerentes', name: 'Gerentes' },
            { id: 'subdirectores', name: 'Subdirectores' },
            { id: 'asesores', name: 'Asesores digitales' }
        ],
        segments: [
            { id: 'bp', name: 'Banca Personal' },
            { id: 'premier', name: 'Premier' },
            { id: 'rbb', name: 'RBB Digital' },
            { id: 'pyme', name: 'PyME' }
        ],
        regions: [
            { id: 'norte', name: 'Norte' },
            { id: 'centro', name: 'Centro' },
            { id: 'metro', name: 'Metropolitana' },
            { id: 'occidente', name: 'Occidente' },
            { id: 'sur', name: 'Sur' }
        ],
        branches: [
            branch('b01', 'Monterrey Centro', 'norte', 'bp', 24, 3, 1, 4),
            branch('b02', 'San Pedro', 'norte', 'premier', 16, 2, 1, 2),
            branch('b03', 'Chihuahua', 'norte', 'bp', 18, 2, 0, 3),
            branch('b04', 'Reforma', 'metro', 'premier', 22, 3, 1, 4),
            branch('b05', 'Polanco', 'metro', 'premier', 20, 3, 1, 3),
            branch('b06', 'Insurgentes', 'metro', 'bp', 26, 3, 1, 5),
            branch('b07', 'Querétaro', 'centro', 'bp', 17, 2, 0, 3),
            branch('b08', 'Puebla', 'centro', 'rbb', 12, 2, 0, 8),
            branch('b09', 'Guadalajara Chapultepec', 'occidente', 'bp', 25, 3, 1, 4),
            branch('b10', 'Zapopan', 'occidente', 'premier', 15, 2, 1, 2),
            branch('b11', 'Mérida', 'sur', 'bp', 14, 2, 0, 3),
            branch('b12', 'Cancún', 'sur', 'pyme', 10, 2, 0, 2),
            branch('b13', 'Centro Digital CDMX', 'metro', 'rbb', 6, 2, 1, 20),
            branch('b14', 'León', 'centro', 'pyme', 11, 2, 0, 2)
        ],
        // Personas ficticias para el selector de "Usuarios específicos".
        users: [
            { id: 'u01', name: 'Ana Torres', email: 'ana.torres@empresa.com', roleId: 'gerentes', branchId: 'b04' },
            { id: 'u02', name: 'Luis Ramírez', email: 'luis.ramirez@empresa.com', roleId: 'ejecutivos', branchId: 'b01' },
            { id: 'u03', name: 'María Fernández', email: 'maria.fernandez@empresa.com', roleId: 'subdirectores', branchId: 'b06' },
            { id: 'u04', name: 'Jorge Castillo', email: 'jorge.castillo@empresa.com', roleId: 'ejecutivos', branchId: 'b09' },
            { id: 'u05', name: 'Paola Mendoza', email: 'paola.mendoza@empresa.com', roleId: 'asesores', branchId: 'b13' },
            { id: 'u06', name: 'Ricardo Vega', email: 'ricardo.vega@empresa.com', roleId: 'gerentes', branchId: 'b07' },
            { id: 'u07', name: 'Sofía Herrera', email: 'sofia.herrera@empresa.com', roleId: 'ejecutivos', branchId: 'b05' },
            { id: 'u08', name: 'Daniel Ortiz', email: 'daniel.ortiz@empresa.com', roleId: 'ejecutivos', branchId: 'b11' },
            { id: 'u09', name: 'Valeria Núñez', email: 'valeria.nunez@empresa.com', roleId: 'gerentes', branchId: 'b10' },
            { id: 'u10', name: 'Héctor Salinas', email: 'hector.salinas@empresa.com', roleId: 'subdirectores', branchId: 'b02' },
            { id: 'u11', name: 'Carla Rojas', email: 'carla.rojas@empresa.com', roleId: 'asesores', branchId: 'b08' },
            { id: 'u12', name: 'Miguel Ángel Prieto', email: 'miguel.prieto@empresa.com', roleId: 'ejecutivos', branchId: 'b14' }
        ]
    };

    // ---------------------------------------------------------------------
    // Respuestas simuladas
    // ---------------------------------------------------------------------

    /** PRNG con semilla para que la demo muestre siempre los mismos resultados. */
    function seeded(seed) {
        var a = seed >>> 0;
        return function () {
            a = (a + 0x6D2B79F5) >>> 0;
            var t = a;
            t = Math.imul(t ^ (t >>> 15), t | 1);
            t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
            return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
        };
    }

    var comments = [
        'Considero que los materiales son claros, pero sería útil incluir más ejemplos prácticos y casos reales.',
        'Me gustaría tener el material antes del arranque de la campaña.',
        'Los videos son muy buenos, pero son largos para verlos en sucursal.',
        'Agregar argumentos para clientes que ya tienen productos con la competencia.',
        'Más material para manejo de objeciones en portabilidad.',
        'La guía comercial es muy completa; la presentación podría ser más corta.',
        'Que el material esté disponible en el celular.',
        'Incluir un resumen de una página con lo más importante.',
        'Mejorar la claridad de las condiciones de la promoción.',
        'Capacitación presencial para los ejecutivos nuevos.',
        'Los correos llegan muy tarde, ya cuando inició la campaña.',
        'Todo bien, solo actualizar las tasas con más frecuencia.'
    ];
    var others = ['Material de WhatsApp', 'Infografías', 'Podcast interno', 'Reunión con el gerente'];

    function weighted(rnd, weights) {
        var total = 0, i;
        for (i = 0; i < weights.length; i++) total += weights[i];
        var r = rnd() * total;
        for (i = 0; i < weights.length; i++) {
            r -= weights[i];
            if (r <= 0) return i;
        }
        return weights.length - 1;
    }

    function decreasing(n) {
        var w = [];
        for (var i = 0; i < n; i++) w.push(n - i + (i === 1 ? 1 : 0));
        return w;
    }

    function fakeAnswer(q, rnd) {
        var v = q.validation || {};
        switch (q.type) {
            case 'singleChoice':
            case 'dropdown':
                return { optionIds: [q.options[weighted(rnd, decreasing(q.options.length))].id] };
            case 'yesNo':
                return { optionIds: [q.options[rnd() < 0.7 ? 0 : 1].id] };
            case 'multipleChoice':
                var ids = $.map(q.options, function (o) { return rnd() < 0.45 ? o.id : null; });
                if (!ids.length) ids.push(q.options[0].id);
                if (v.maxSelections) ids = ids.slice(0, v.maxSelections);
                var a = { optionIds: ids };
                if (q.allowOther && rnd() < 0.12 && ids.length < (v.maxSelections || 99)) {
                    ids.push(SurveyRenderer.OTHER);
                    a.otherText = others[Math.floor(rnd() * others.length)];
                }
                return a;
            case 'text':
                return { text: comments[Math.floor(rnd() * comments.length)] };
            case 'scale':
                var min = v.scaleMin || 1, max = v.scaleMax || 5;
                return { number: Math.min(max, min + Math.floor((max - min + 1) * (0.45 + 0.55 * rnd()))) };
            case 'number':
                return { number: Math.round((v.minValue || 0) + rnd() * ((v.maxValue || 100) - (v.minValue || 0)) * 0.4) };
            case 'date':
                var d = new Date(2026, 6, 1 + Math.floor(rnd() * 90));
                return { date: d.toISOString().substring(0, 10) };
            case 'matrix':
                var m = {};
                $.each(q.matrix.rows, function (_, r) {
                    m[r.id] = [q.matrix.columns[weighted(rnd, [0.5, 1, 2, 4, 3].slice(0, q.matrix.columns.length))].id];
                });
                return { matrix: m };
            case 'dependent':
                var picked = [];
                var pick = function (parentId, depth) {
                    var kids = $.grep(q.dependent.options, function (o) { return o.parentId === parentId; });
                    if (!kids.length) return;
                    var take = depth === 0 ? 1 + Math.floor(rnd() * 2) : 1 + Math.floor(rnd() * 2);
                    $.each(SurveyUtils.shuffle(kids).slice(0, take), function (_, k) {
                        if (depth === 0 && picked.length && rnd() < 0.5) return;
                        picked.push(k.id);
                        pick(k.id, depth + 1);
                    });
                };
                // Banca Personal es la más consultada.
                var roots = $.grep(q.dependent.options, function (o) { return o.level === 0; });
                picked.push(roots[weighted(rnd, [6, 3, 2].slice(0, roots.length))].id);
                pick(picked[0], 1);
                return { optionIds: picked };
        }
        return null;
    }

    /**
     * Genera respuestas que respetan la lógica condicional de la versión
     * (si contestan "No aplica" en P4, la respuesta termina ahí).
     */
    function generateResponses(version, count, seed) {
        var rnd = seeded(seed || 7);
        var questions = SurveyLogic.sorted(version.questions);
        var start = new Date((version.settings.startDate || '2026-09-01') + 'T09:00:00');
        var list = [];

        for (var i = 0; i < count; i++) {
            var answers = {};
            for (var k = 0; k < questions.length; k++) {
                var q = questions[k];
                var state = SurveyLogic.computeVisibility(questions, answers);
                if (state.endedAt || state.visible[q.id] === false) continue;
                if (!q.required && rnd() < 0.18) continue;
                var a = fakeAnswer(q, rnd);
                if (a) answers[q.id] = a;
            }
            var submitted = new Date(start.getTime() + Math.floor(rnd() * 34 * 24 * 3600 * 1000));
            list.push({ id: 'r' + (i + 1), submittedAt: submitted.toISOString().substring(0, 19), answers: answers });
        }
        return list;
    }

    return {
        buildSurvey: buildSurvey,
        catalogs: catalogs,
        generateResponses: generateResponses
    };
})(jQuery);
