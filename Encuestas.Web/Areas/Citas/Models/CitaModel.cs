using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ModuloCitas.Models
{
    /// <summary>
    /// Formulario de cita. En el portal, usa el modelo que ya tiene tu formulario: para Outlook solo
    /// importan asunto, inicio, fin, lugar y notas.
    /// </summary>
    public class CitaModel : IValidatableObject
    {
        public int Id { get; set; }

        /// <summary>Quién la registró (para que nadie descargue citas de otros).</summary>
        public string Usuario { get; set; }

        [Required(ErrorMessage = "Escribe el asunto de la cita.")]
        [StringLength(120, ErrorMessage = "El asunto admite hasta 120 caracteres.")]
        public string Asunto { get; set; }

        [Required(ErrorMessage = "Elige la fecha.")]
        public DateTime? Fecha { get; set; }

        [Required(ErrorMessage = "Elige la hora de inicio.")]
        public TimeSpan? HoraInicio { get; set; }

        [Required(ErrorMessage = "Elige la hora de fin.")]
        public TimeSpan? HoraFin { get; set; }

        [StringLength(150, ErrorMessage = "El lugar admite hasta 150 caracteres.")]
        public string Lugar { get; set; }

        [StringLength(1000, ErrorMessage = "Las notas admiten hasta 1000 caracteres.")]
        public string Notas { get; set; }

        /// <summary>Minutos antes para el recordatorio de Outlook (0 = sin recordatorio).</summary>
        [Range(0, 10080)]
        public int Recordatorio { get; set; }

        public DateTime Inicio { get { return Fecha.GetValueOrDefault().Date + HoraInicio.GetValueOrDefault(); } }
        public DateTime Fin { get { return Fecha.GetValueOrDefault().Date + HoraFin.GetValueOrDefault(); } }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!Fecha.HasValue || !HoraInicio.HasValue || !HoraFin.HasValue) yield break;
            if (Fin <= Inicio)
                yield return new ValidationResult("La hora de fin debe ser posterior a la de inicio.", new[] { "HoraFin" });
            if (Inicio < DateTime.Now)
                yield return new ValidationResult("La cita debe ser en una fecha y hora futuras.", new[] { "Fecha" });
        }
    }
}
