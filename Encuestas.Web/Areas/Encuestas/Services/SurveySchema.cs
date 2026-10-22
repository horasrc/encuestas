using System.Configuration;
using System.Text.RegularExpressions;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Esquema SQL donde viven las tablas del módulo: appSetting "Surveys:Schema" (por defecto "enc").
    /// Las consultas se escriben con el prefijo "enc." y aquí se cambia por el esquema configurado,
    /// por ejemplo para usar un esquema que el portal ya tiene (IC) cuando no se permite crear uno nuevo.
    /// Los scripts para SSMS de ese esquema se generan con tools\esquema-sql.ps1.
    /// </summary>
    public static class SurveySchema
    {
        public const string Default = "enc";

        private static readonly Regex Prefix = new Regex(@"\benc\.", RegexOptions.Compiled);
        private static readonly Regex ValidName = new Regex(@"^[A-Za-z_][A-Za-z0-9_]{0,127}$");
        private static string _name;

        /// <summary>Nombre del esquema configurado (se valida una sola vez).</summary>
        public static string Name
        {
            get { return _name ?? (_name = Validate(ConfigurationManager.AppSettings["Surveys:Schema"])); }
        }

        /// <summary>Vacío = "enc". Solo letras, números y guion bajo, para poder insertarlo en el SQL.</summary>
        public static string Validate(string value)
        {
            var name = (value ?? "").Trim();
            if (name.Length == 0) return Default;
            if (!ValidName.IsMatch(name))
                throw new ConfigurationErrorsException("Surveys:Schema debe ser un nombre de esquema SQL simple (letras, números y _): '" + name + "'.");
            return name;
        }

        /// <summary>Cambia el prefijo "enc." de una consulta por el esquema configurado.</summary>
        public static string Apply(string sql)
        {
            return Apply(sql, Name);
        }

        public static string Apply(string sql, string schema)
        {
            return schema == Default ? sql : Prefix.Replace(sql, "[" + schema + "].");
        }
    }
}
