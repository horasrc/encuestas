using System;
using System.Text;

namespace ModuloCitas.Services
{
    /// <summary>Datos de una cita para el calendario.</summary>
    public class EventoCalendario
    {
        /// <summary>Identificador estable (ej. "cita-123@portal"): si se abre dos veces, Outlook la reconoce como la misma.</summary>
        public string Uid { get; set; }
        public string Asunto { get; set; }
        /// <summary>Hora local del servidor; se convierte a UTC y Outlook la muestra en la zona del usuario.</summary>
        public DateTime Inicio { get; set; }
        public DateTime Fin { get; set; }
        public string Lugar { get; set; }
        public string Descripcion { get; set; }
        /// <summary>Minutos antes para el recordatorio. 0 = sin recordatorio.</summary>
        public int RecordatorioMinutos { get; set; }
    }

    /// <summary>
    /// Genera un archivo iCalendar (.ics, RFC 5545). Al abrirlo, Outlook muestra la cita llena
    /// y el usuario solo da "Guardar y cerrar". No necesita internet, Exchange ni permisos.
    ///
    /// Es la única pieza que hay que copiar al portal para agregar "Agregar a Outlook" a cualquier
    /// formulario (ver CITAS-OUTLOOK.md).
    /// </summary>
    public static class CalendarioIcs
    {
        public const string ContentType = "text/calendar";

        public static byte[] Crear(EventoCalendario e)
        {
            if (e == null) throw new ArgumentNullException("e");
            if (e.Fin <= e.Inicio) throw new ArgumentException("La hora de fin debe ser posterior a la de inicio.");

            var sb = new StringBuilder();
            Linea(sb, "BEGIN:VCALENDAR");
            Linea(sb, "VERSION:2.0");
            Linea(sb, "PRODID:-//Portal interno//Citas//ES");
            Linea(sb, "CALSCALE:GREGORIAN");
            Linea(sb, "METHOD:PUBLISH");                // cita propia, no una invitación a reunión
            Linea(sb, "BEGIN:VEVENT");
            Linea(sb, "UID:" + Texto(string.IsNullOrEmpty(e.Uid) ? Guid.NewGuid().ToString("N") + "@portal" : e.Uid));
            Linea(sb, "DTSTAMP:" + Utc(DateTime.Now));
            Linea(sb, "DTSTART:" + Utc(e.Inicio));
            Linea(sb, "DTEND:" + Utc(e.Fin));
            Linea(sb, "SUMMARY:" + Texto(string.IsNullOrWhiteSpace(e.Asunto) ? "Cita" : e.Asunto));
            if (!string.IsNullOrWhiteSpace(e.Lugar)) Linea(sb, "LOCATION:" + Texto(e.Lugar));
            if (!string.IsNullOrWhiteSpace(e.Descripcion)) Linea(sb, "DESCRIPTION:" + Texto(e.Descripcion));
            Linea(sb, "STATUS:CONFIRMED");
            Linea(sb, "TRANSP:OPAQUE");                 // aparece como "Ocupado"
            if (e.RecordatorioMinutos > 0)
            {
                Linea(sb, "BEGIN:VALARM");
                Linea(sb, "TRIGGER:-PT" + e.RecordatorioMinutos + "M");
                Linea(sb, "ACTION:DISPLAY");
                Linea(sb, "DESCRIPTION:Recordatorio");
                Linea(sb, "END:VALARM");
            }
            Linea(sb, "END:VEVENT");
            Linea(sb, "END:VCALENDAR");

            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        /// <summary>Nombre de archivo seguro, ej. "cita-123.ics".</summary>
        public static string NombreArchivo(string baseName)
        {
            var limpio = new StringBuilder();
            foreach (var ch in baseName ?? "cita")
                limpio.Append(char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' ? ch : '-');
            return (limpio.Length == 0 ? "cita" : limpio.ToString()) + ".ics";
        }

        private static string Utc(DateTime value)
        {
            var utc = value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
            return utc.ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Escapa los caracteres especiales del formato y quita los de control.</summary>
        private static string Texto(string s)
        {
            var sb = new StringBuilder();
            foreach (var ch in s.Replace("\r\n", "\n").Replace('\r', '\n'))
            {
                switch (ch)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case ';': sb.Append("\\;"); break;
                    case ',': sb.Append("\\,"); break;
                    case '\n': sb.Append("\\n"); break;
                    default: if (!char.IsControl(ch)) sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Agrega una línea terminada en CRLF, partida cada 75 bytes como pide el estándar
        /// (las continuaciones empiezan con un espacio). No corta caracteres con acento.
        /// </summary>
        private static void Linea(StringBuilder sb, string line)
        {
            var bytes = 0;
            for (var i = 0; i < line.Length; i++)
            {
                var len = Encoding.UTF8.GetByteCount(line.Substring(i, char.IsHighSurrogate(line[i]) && i + 1 < line.Length ? 2 : 1));
                if (bytes + len > 75)
                {
                    sb.Append("\r\n ");
                    bytes = 1;   // el espacio de la continuación cuenta
                }
                sb.Append(line[i]);
                if (char.IsHighSurrogate(line[i]) && i + 1 < line.Length) sb.Append(line[++i]);
                bytes += len;
            }
            sb.Append("\r\n");
        }
    }
}
