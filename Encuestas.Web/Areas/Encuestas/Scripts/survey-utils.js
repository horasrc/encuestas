/* =========================================================================
 * survey-utils.js
 * Utilidades compartidas por todos los módulos del constructor y por la
 * página de respuesta: IDs, clonado, escape HTML, fechas, toasts,
 * confirmaciones y ordenamiento por arrastre (HTML5 drag & drop).
 * ========================================================================= */
var SurveyUtils = (function ($) {
    'use strict';

    var seq = 0;

    /** Genera un ID temporal de cliente. El backend lo guarda como ClientKey. */
    function newId(prefix) {
        seq += 1;
        return (prefix || 'id') + '_' + Date.now().toString(36) + seq.toString(36) + Math.floor(Math.random() * 1296).toString(36);
    }

    /** Copia profunda de objetos planos (los modelos son JSON puros). */
    function clone(obj) {
        return obj === undefined ? undefined : JSON.parse(JSON.stringify(obj));
    }

    function escapeHtml(value) {
        return String(value === null || value === undefined ? '' : value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    /** Baraja (Fisher-Yates) una copia del arreglo. */
    function shuffle(list) {
        var a = list.slice();
        for (var i = a.length - 1; i > 0; i--) {
            var j = Math.floor(Math.random() * (i + 1));
            var t = a[i]; a[i] = a[j]; a[j] = t;
        }
        return a;
    }

    /** 'yyyy-mm-dd' o ISO -> 'dd/mm/yyyy'. */
    function formatDate(value) {
        if (!value) return '—';
        var m = String(value).match(/^(\d{4})-(\d{2})-(\d{2})/);
        return m ? m[3] + '/' + m[2] + '/' + m[1] : String(value);
    }

    function todayIso() {
        var d = new Date();
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    }

    function pad(n) { return (n < 10 ? '0' : '') + n; }

    /** Convierte a número o null (inputs vacíos de validaciones). */
    function toNumberOrNull(value) {
        if (value === '' || value === null || value === undefined) return null;
        var n = Number(value);
        return isNaN(n) ? null : n;
    }

    /** Asigna un valor siguiendo una ruta "a.b.c" (crea objetos intermedios). */
    function setPath(obj, path, value) {
        var parts = path.split('.');
        var target = obj;
        for (var i = 0; i < parts.length - 1; i++) {
            if (target[parts[i]] === null || typeof target[parts[i]] !== 'object') target[parts[i]] = {};
            target = target[parts[i]];
        }
        target[parts[parts.length - 1]] = value;
    }

    function getPath(obj, path) {
        var parts = path.split('.');
        var target = obj;
        for (var i = 0; i < parts.length; i++) {
            if (target === null || target === undefined) return undefined;
            target = target[parts[i]];
        }
        return target;
    }

    /** "1 respuesta" / "3 respuestas" (plural por defecto: singular + "s"). */
    function count(n, singular, plural) {
        n = Number(n) || 0;
        return n.toLocaleString('es-MX') + ' ' + (n === 1 ? singular : (plural || singular + 's'));
    }

    function percent(part, total) {
        return total ? Math.round((part / total) * 1000) / 10 : 0;
    }

    /** Notificación no intrusiva (Bootstrap toast). variant: success | danger | info */
    function toast(message, variant) {
        var $container = $('#toastContainer');
        if (!$container.length) {
            $container = $('<div id="toastContainer" class="toast-container position-fixed bottom-0 end-0 p-3"></div>').appendTo('body');
        }
        var icon = variant === 'danger' ? 'bi-exclamation-octagon' : variant === 'success' ? 'bi-check-circle' : 'bi-info-circle';
        var $toast = $(
            '<div class="toast sv-toast sv-toast-' + (variant || 'info') + '" role="status" aria-live="polite" aria-atomic="true">' +
            '  <div class="d-flex">' +
            '    <div class="toast-body"><i class="bi ' + icon + ' me-2"></i>' + escapeHtml(message) + '</div>' +
            '    <button type="button" class="btn-close me-2 m-auto" data-bs-dismiss="toast" aria-label="Cerrar"></button>' +
            '  </div>' +
            '</div>').appendTo($container);
        var instance = bootstrap.Toast.getOrCreateInstance($toast[0], { delay: 3500 });
        $toast.on('hidden.bs.toast', function () { $toast.remove(); });
        instance.show();
    }

    /**
     * Confirmación con el modal genérico #confirmModal.
     * Devuelve una promesa jQuery que se resuelve si el usuario confirma.
     */
    function confirmDialog(options) {
        var opts = $.extend({ title: 'Confirmar', message: '', confirmText: 'Aceptar', danger: false }, options);
        var deferred = $.Deferred();
        var $modal = $('#confirmModal');
        $modal.find('.modal-title').text(opts.title);
        $modal.find('.modal-body').html(opts.message);
        var $btn = $modal.find('[data-confirm]')
            .text(opts.confirmText)
            .toggleClass('btn-accent', !opts.danger)
            .toggleClass('btn-danger', !!opts.danger);

        var confirmed = false;
        var modal = bootstrap.Modal.getOrCreateInstance($modal[0]);
        $btn.off('click.confirm').on('click.confirm', function () {
            confirmed = true;
            modal.hide();
        });
        // Bootstrap ignora hide() mientras el modal se está abriendo: si se confirmó
        // durante la animación (clic rápido / doble clic), se cierra al terminar de abrir.
        $modal.off('shown.bs.modal.confirm').on('shown.bs.modal.confirm', function () {
            if (confirmed) modal.hide();
        });
        $modal.off('hidden.bs.modal.confirm').on('hidden.bs.modal.confirm', function () {
            $modal.off('shown.bs.modal.confirm hidden.bs.modal.confirm');
            if (confirmed) deferred.resolve(); else deferred.reject();
        });
        modal.show();
        return deferred.promise();
    }

    /**
     * Ordenamiento por arrastre sin librerías externas.
     * - El arrastre solo inicia desde un elemento .drag-handle.
     * - Solo reordena entre hermanos (no mezcla listas anidadas).
     * - onReorder recibe el arreglo de data-sort-id en el nuevo orden.
     * En pantallas táctiles se usan los botones Subir/Bajar como alternativa.
     */
    function sortable($container, itemSelector, onReorder) {
        var dragEl = null;

        $container.on('mousedown', '.drag-handle', function () {
            $(this).closest(itemSelector).attr('draggable', 'true');
        });
        $(document).on('mouseup', function () {
            if (!dragEl) $container.find('[draggable="true"]').attr('draggable', 'false');
        });

        $container.on('dragstart', itemSelector, function (e) {
            if (this.getAttribute('draggable') !== 'true') return;
            e.stopPropagation();
            dragEl = this;
            e.originalEvent.dataTransfer.effectAllowed = 'move';
            e.originalEvent.dataTransfer.setData('text/plain', $(this).attr('data-sort-id') || '');
            $(this).addClass('sv-dragging');
        });

        $container.on('dragover', itemSelector, function (e) {
            if (!dragEl || dragEl === this || dragEl.parentNode !== this.parentNode) return;
            e.preventDefault();
            e.stopPropagation();
            var rect = this.getBoundingClientRect();
            var after = e.originalEvent.clientY > rect.top + rect.height / 2;
            this.parentNode.insertBefore(dragEl, after ? this.nextSibling : this);
        });

        $container.on('drop', itemSelector, function (e) {
            if (dragEl) e.preventDefault();
        });

        $container.on('dragend', itemSelector, function (e) {
            if (dragEl !== this) return;
            e.stopPropagation();
            var parent = this.parentNode;
            $(this).removeClass('sv-dragging').attr('draggable', 'false');
            dragEl = null;
            var ids = $(parent).children(itemSelector).map(function () {
                return $(this).attr('data-sort-id');
            }).get();
            onReorder(ids, parent);
        });
    }

    /** Etiqueta, clase CSS e icono de cada estado de versión. */
    var STATUS = {
        Draft: { label: 'Borrador', css: 'sv-status-draft', icon: 'bi-pencil' },
        Published: { label: 'Publicada', css: 'sv-status-published', icon: 'bi-broadcast' },
        Closed: { label: 'Cerrada', css: 'sv-status-closed', icon: 'bi-lock' },
        Archived: { label: 'Archivada', css: 'sv-status-archived', icon: 'bi-archive' }
    };

    function statusInfo(status) { return STATUS[status] || STATUS.Draft; }

    function statusBadge(status) {
        var s = statusInfo(status);
        return '<span class="badge sv-status ' + s.css + '"><i class="bi ' + s.icon + '"></i> ' + s.label + '</span>';
    }

    /** Copia de una versión con IDs nuevos (equivalente a SurveyVersionCloner.cs). */
    function cloneVersion(version, newNumber) {
        var copy = clone(version);
        var map = {};
        function remapId(obj) {
            var nid = newId('x');
            map[obj.id] = nid;
            obj.id = nid;
        }

        copy.id = newId('v');
        copy.number = newNumber;
        copy.status = 'Draft';
        copy.responseCount = 0;
        copy.createdAt = new Date().toISOString().substring(0, 19);
        copy.publishedAt = null;

        $.each(copy.questions, function (_, q) {
            remapId(q);
            $.each(q.options || [], function (_, o) { remapId(o); });
            if (q.matrix) {
                $.each(q.matrix.rows, function (_, o) { remapId(o); });
                $.each(q.matrix.columns, function (_, o) { remapId(o); });
            }
            if (q.dependent) {
                $.each(q.dependent.levels, function (_, l) { remapId(l); });
                $.each(q.dependent.options, function (_, o) { remapId(o); });
            }
            $.each(q.rules || [], function (_, r) { remapId(r); });
        });

        // Segunda pasada: referencias cruzadas.
        $.each(copy.questions, function (_, q) {
            if (q.parentQuestionId && map[q.parentQuestionId]) q.parentQuestionId = map[q.parentQuestionId];
            $.each(q.options || [], function (_, o) { if (o.parentId && map[o.parentId]) o.parentId = map[o.parentId]; });
            if (q.dependent) {
                $.each(q.dependent.options, function (_, o) { if (o.parentId && map[o.parentId]) o.parentId = map[o.parentId]; });
            }
            $.each(q.rules || [], function (_, r) {
                if (r.value && map[r.value]) r.value = map[r.value];
                if (r.targetQuestionId && map[r.targetQuestionId]) r.targetQuestionId = map[r.targetQuestionId];
            });
        });
        return copy;
    }

    /**
     * Dentro del layout del portal, el contenedor del contenido puede ser más ancho que la
     * ventana: el botón «Nueva encuesta», el buscador y parte de la tabla quedaban fuera de
     * la vista. Si la página del módulo se sale por la derecha, se limita a lo visible.
     */
    function fitToViewport() {
        $('.sv-page').each(function () {
            this.style.maxWidth = '';
            this.style.marginLeft = '';
            this.style.marginRight = '';
            var rect = this.getBoundingClientRect();
            var viewport = document.documentElement.clientWidth;
            if (rect.right <= viewport + 1) return;
            // Sin márgenes automáticos: centrada en un contenedor ancho quedaría otra vez fuera de la vista.
            this.style.marginLeft = '0';
            this.style.marginRight = '0';
            var left = this.getBoundingClientRect().left;
            var available = viewport - left - 16;
            if (available >= 320) this.style.maxWidth = available + 'px';
        });
    }

    $(function () {
        fitToViewport();
        var timer = null;
        $(window).on('resize', function () { clearTimeout(timer); timer = setTimeout(fitToViewport, 150); });
    });

    return {
        newId: newId,
        clone: clone,
        escapeHtml: escapeHtml,
        shuffle: shuffle,
        formatDate: formatDate,
        todayIso: todayIso,
        toNumberOrNull: toNumberOrNull,
        setPath: setPath,
        getPath: getPath,
        percent: percent,
        count: count,
        toast: toast,
        confirmDialog: confirmDialog,
        sortable: sortable,
        statusInfo: statusInfo,
        statusBadge: statusBadge,
        cloneVersion: cloneVersion,
        fitToViewport: fitToViewport
    };
})(jQuery);
