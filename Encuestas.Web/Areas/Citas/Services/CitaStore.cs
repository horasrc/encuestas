using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ModuloCitas.Models;

namespace ModuloCitas.Services
{
    /// <summary>
    /// Almacenamiento EN MEMORIA solo para la demostración (se borra al reiniciar el sitio).
    /// En el portal, aquí va la tabla de citas que ya existe: guardar y obtener por id.
    /// </summary>
    public static class CitaStore
    {
        private static readonly ConcurrentDictionary<int, CitaModel> Citas = new ConcurrentDictionary<int, CitaModel>();
        private static int _ultimoId;

        public static int Guardar(CitaModel cita)
        {
            cita.Id = Interlocked.Increment(ref _ultimoId);
            Citas[cita.Id] = cita;
            return cita.Id;
        }

        /// <summary>La cita solo se devuelve a quien la registró.</summary>
        public static CitaModel Obtener(int id, string usuario)
        {
            CitaModel cita;
            return Citas.TryGetValue(id, out cita) && string.Equals(cita.Usuario, usuario, StringComparison.OrdinalIgnoreCase)
                ? cita
                : null;
        }

        public static IList<CitaModel> Proximas(string usuario)
        {
            return Citas.Values
                .Where(c => string.Equals(c.Usuario, usuario, StringComparison.OrdinalIgnoreCase) && c.Fin >= DateTime.Now)
                .OrderBy(c => c.Inicio)
                .ToList();
        }
    }
}
