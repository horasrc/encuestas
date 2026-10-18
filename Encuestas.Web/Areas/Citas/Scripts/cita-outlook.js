/*
 * cita-outlook.js
 * Genera EN EL NAVEGADOR la cita para Outlook (.ics) con los datos del formulario
 * "Registro de citas / visitas" y la descarga. Al abrir el archivo, Outlook muestra la cita
 * llena y el usuario da "Guardar y cerrar". No usa el servidor, la base de datos ni internet.
 *
 * Uso: en guardarCaptura(), cuando el guardado fue exitoso:
 *     CitaOutlook.descargar(formData);
 *
 * Datos que incluye (de formData): clientName, appointmentDate, appointmentType,
 * accompaniment, comments, isCustomer. Los textos de "Tipo de cita", "Acompañamiento" y
 * "¿Es cliente?" se leen de lo que ve el usuario en la pantalla, no del valor interno.
 *
 * La fecha no tiene hora: la cita entra como evento de TODO EL DÍA, marcada como "Libre"
 * (no bloquea el día) y con recordatorio a las 9:00 a.m. del día anterior.
 * Solo se genera si la fecha es hoy o futura (una visita que ya pasó no se agenda).
 */
(function (window, document) {
    'use strict';

    // Ids de los controles del formulario (cámbialos aquí si cambian en la vista).
    var IDS = {
        tipoCita: 'appointmentType',
        acompanamiento: 'accompaniment',
        esClienteName: 'isCustomer'
    };
    var RECORDATORIO = '-PT15H';   // 15 h antes de las 00:00 del día = 9:00 a.m. del día anterior

    function pad(n) { return (n < 10 ? '0' : '') + n; }

    /** "2026-10-20" (valor de <input type="date">) o "20/10/2026" -> Date local; si no, null. */
    function parseFecha(valor) {
        var s = String(valor || '');
        var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(s);
        if (m) return new Date(+m[1], +m[2] - 1, +m[3]);
        m = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(s);
        if (m) return new Date(+m[3], +m[2] - 1, +m[1]);
        return null;
    }

    function fechaIcs(d) { return d.getFullYear() + pad(d.getMonth() + 1) + pad(d.getDate()); }

    function utcIcs(d) {
        return d.getUTCFullYear() + pad(d.getUTCMonth() + 1) + pad(d.getUTCDate()) + 'T' +
            pad(d.getUTCHours()) + pad(d.getUTCMinutes()) + pad(d.getUTCSeconds()) + 'Z';
    }

    /** Escapa los caracteres especiales del formato iCalendar. */
    function texto(s) {
        return String(s == null ? '' : s)
            .replace(/\r\n?/g, '\n')
            .replace(/\\/g, '\\\\')
            .replace(/;/g, '\\;')
            .replace(/,/g, '\\,')
            .replace(/\n/g, '\\n')
            .replace(/[\x00-\x08\x0B-\x1F\x7F]/g, '');
    }

    function bytesUtf8(code) {
        if (code < 0x80) return 1;
        if (code < 0x800) return 2;
        if (code >= 0xD800 && code <= 0xDBFF) return 4;   // par sustituto (emoji): 4 bytes en total
        return 3;
    }

    /** Línea terminada en CRLF, partida cada 75 bytes como pide el estándar (sin cortar acentos). */
    function linea(out, s) {
        var bytes = 0, res = '';
        for (var i = 0; i < s.length; i++) {
            var code = s.charCodeAt(i);
            var ch = s.charAt(i);
            if (code >= 0xD800 && code <= 0xDBFF && i + 1 < s.length) ch += s.charAt(++i);
            var len = bytesUtf8(code);
            if (bytes + len > 75) { res += '\r\n '; bytes = 1; }
            res += ch;
            bytes += len;
        }
        out.push(res + '\r\n');
    }

    function textoSeleccionado(id) {
        var sel = document.getElementById(id);
        if (!sel || sel.selectedIndex < 0 || !sel.value) return '';
        return (sel.options[sel.selectedIndex].text || '').replace(/^\s+|\s+$/g, '');
    }

    function textoEsCliente(valor) {
        var radio = document.querySelector && document.querySelector('input[name="' + IDS.esClienteName + '"]:checked');
        var label = radio && radio.id ? document.querySelector('label[for="' + radio.id + '"]') : null;
        if (label && label.textContent) return label.textContent.replace(/^\s+|\s+$/g, '');
        if (/^(s|si|sí|1|true|y|yes)$/i.test(String(valor))) return 'Sí';
        if (/^(n|no|0|false)$/i.test(String(valor))) return 'No';
        return valor == null ? '' : String(valor);
    }

    /**
     * Arma el texto del archivo .ics. Devuelve null si no hay fecha válida o si ya pasó.
     * datos: { clientName, appointmentDate, tipoCita, acompanamiento, comments, esCliente }
     */
    function crear(datos, ahora) {
        ahora = ahora || new Date();
        var dia = parseFecha(datos.appointmentDate);
        if (!dia) return null;
        var hoy = new Date(ahora.getFullYear(), ahora.getMonth(), ahora.getDate());
        if (dia < hoy) return null;
        var siguiente = new Date(dia.getFullYear(), dia.getMonth(), dia.getDate() + 1);

        var nombre = String(datos.clientName || '').replace(/^\s+|\s+$/g, '');
        var asunto = 'Cita' + (datos.tipoCita ? ': ' + datos.tipoCita : '') + (nombre ? ' - ' + nombre : '');

        var notas = [];
        if (nombre) notas.push('Cliente: ' + nombre);
        if (datos.esCliente) notas.push('¿Es cliente?: ' + datos.esCliente);
        if (datos.tipoCita) notas.push('Tipo de cita: ' + datos.tipoCita);
        if (datos.acompanamiento) notas.push('Acompañamiento: ' + datos.acompanamiento);
        if (datos.comments) notas.push('Comentarios: ' + datos.comments);

        var uid = 'cita-' + fechaIcs(dia) + '-' + ahora.getTime() + '-' + Math.floor(Math.random() * 1e9) + '@radar';
        var out = [];
        linea(out, 'BEGIN:VCALENDAR');
        linea(out, 'VERSION:2.0');
        linea(out, 'PRODID:-//Portal interno//Citas//ES');
        linea(out, 'CALSCALE:GREGORIAN');
        linea(out, 'METHOD:PUBLISH');
        linea(out, 'BEGIN:VEVENT');
        linea(out, 'UID:' + uid);
        linea(out, 'DTSTAMP:' + utcIcs(ahora));
        linea(out, 'DTSTART;VALUE=DATE:' + fechaIcs(dia));
        linea(out, 'DTEND;VALUE=DATE:' + fechaIcs(siguiente));
        linea(out, 'SUMMARY:' + texto(asunto));
        if (notas.length) linea(out, 'DESCRIPTION:' + texto(notas.join('\n')));
        linea(out, 'TRANSP:TRANSPARENT');                      // no bloquea el día ("Libre")
        linea(out, 'X-MICROSOFT-CDO-BUSYSTATUS:FREE');
        linea(out, 'X-MICROSOFT-CDO-ALLDAYEVENT:TRUE');
        linea(out, 'BEGIN:VALARM');
        linea(out, 'TRIGGER:' + RECORDATORIO);
        linea(out, 'ACTION:DISPLAY');
        linea(out, 'DESCRIPTION:Recordatorio');
        linea(out, 'END:VALARM');
        linea(out, 'END:VEVENT');
        linea(out, 'END:VCALENDAR');
        return out.join('');
    }

    /**
     * Toma el formData de guardarCaptura(), arma la cita y la descarga.
     * Devuelve true si se descargó; false si no hay fecha o la fecha ya pasó.
     */
    function descargar(formData) {
        var datos = {
            clientName: formData.clientName,
            appointmentDate: formData.appointmentDate,
            tipoCita: textoSeleccionado(IDS.tipoCita) || formData.appointmentType || '',
            acompanamiento: textoSeleccionado(IDS.acompanamiento) || formData.accompaniment || '',
            comments: formData.comments,
            esCliente: textoEsCliente(formData.isCustomer)
        };
        var ics = crear(datos);
        if (!ics) return false;

        var nombreArchivo = 'cita-' + String(datos.appointmentDate).replace(/[^0-9]/g, '') + '.ics';
        var blob = new Blob([ics], { type: 'text/calendar;charset=utf-8' });

        if (window.navigator && window.navigator.msSaveOrOpenBlob) {      // Internet Explorer / Edge antiguo
            window.navigator.msSaveOrOpenBlob(blob, nombreArchivo);
            return true;
        }
        var url = (window.URL || window.webkitURL).createObjectURL(blob);
        var a = document.createElement('a');
        a.href = url;
        a.download = nombreArchivo;
        a.style.display = 'none';
        document.body.appendChild(a);
        a.click();
        setTimeout(function () { document.body.removeChild(a); (window.URL || window.webkitURL).revokeObjectURL(url); }, 1000);
        return true;
    }

    window.CitaOutlook = { descargar: descargar, crear: crear };
})(window, document);
