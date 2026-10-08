using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace ModuloEncuestas.Services
{
    /// <summary>
    /// Catálogos de segmentación desde las tablas Org* (Database/org-schema.sql).
    /// Los resultados se guardan en caché unos minutos (Surveys:CatalogCacheMinutes)
    /// porque cambian poco y se consultan en cada envío cuando EnforceAudience = true.
    ///
    /// La plantilla por sucursal (Headcount) se calcula contando colaboradores
    /// activos por sucursal y rol, así la audiencia estimada es la real.
    ///
    /// NOTA: Users devuelve todos los colaboradores activos; el constructor los usa
    /// para buscar "usuarios específicos" en el navegador. Con decenas de miles de
    /// usuarios convendría un endpoint de búsqueda en lugar de enviar la lista.
    /// </summary>
    public class SqlOrgCatalogProvider : IOrgCatalogProvider
    {
        private static readonly object Sync = new object();
        private static OrgCatalogs _cached;
        private static DateTime _expires = DateTime.MinValue;

        private readonly string _connectionString;
        private readonly TimeSpan _cacheDuration;

        public SqlOrgCatalogProvider(string connectionString, TimeSpan cacheDuration)
        {
            if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentException("Falta la cadena de conexión 'SurveysDb'.");
            _connectionString = connectionString;
            _cacheDuration = cacheDuration;
        }

        public OrgCatalogs GetCatalogs()
        {
            lock (Sync)
            {
                if (_cached != null && DateTime.UtcNow < _expires) return _cached;
                _cached = Load();
                _expires = DateTime.UtcNow.Add(_cacheDuration);
                return _cached;
            }
        }

        /// <summary>Descarta la caché (por ejemplo, tras una sincronización de RH).</summary>
        public static void Invalidate()
        {
            lock (Sync) _expires = DateTime.MinValue;
        }

        private OrgCatalogs Load()
        {
            const string sql = @"
SELECT RoleId, Name FROM enc.OrgRole WHERE IsActive = 1 ORDER BY SortOrder, Name;
SELECT SegmentId, Name FROM enc.OrgSegment WHERE IsActive = 1 ORDER BY SortOrder, Name;
SELECT RegionId, Name FROM enc.OrgRegion WHERE IsActive = 1 ORDER BY SortOrder, Name;
SELECT BranchId, Name, RegionId, SegmentId FROM enc.OrgBranch WHERE IsActive = 1 ORDER BY Name;
SELECT BranchId, RoleId, COUNT(*) FROM enc.OrgUser WHERE IsActive = 1 GROUP BY BranchId, RoleId;
SELECT UserId, Login, Name, Email, RoleId, BranchId FROM enc.OrgUser WHERE IsActive = 1 ORDER BY Name;";

            var c = new OrgCatalogs();
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                conn.Open();
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read()) c.Roles.Add(new CatalogItem { Id = r.GetString(0), Name = r.GetString(1) });
                    r.NextResult();
                    while (r.Read()) c.Segments.Add(new CatalogItem { Id = r.GetString(0), Name = r.GetString(1) });
                    r.NextResult();
                    while (r.Read()) c.Regions.Add(new CatalogItem { Id = r.GetString(0), Name = r.GetString(1) });

                    r.NextResult();
                    var branches = new Dictionary<string, BranchItem>();
                    while (r.Read())
                    {
                        var b = new BranchItem
                        {
                            Id = r.GetString(0), Name = r.GetString(1), RegionId = r.GetString(2), SegmentId = r.GetString(3),
                            Headcount = new Dictionary<string, int>()
                        };
                        branches[b.Id] = b;
                        c.Branches.Add(b);
                    }

                    r.NextResult();
                    while (r.Read())
                    {
                        BranchItem b;
                        if (branches.TryGetValue(r.GetString(0), out b)) b.Headcount[r.GetString(1)] = r.GetInt32(2);
                    }

                    r.NextResult();
                    while (r.Read())
                    {
                        c.Users.Add(new UserItem
                        {
                            Id = r.GetString(0),
                            Login = r.GetString(1),
                            Name = r.GetString(2),
                            Email = r.IsDBNull(3) ? null : r.GetString(3),
                            RoleId = r.GetString(4),
                            BranchId = r.GetString(5)
                        });
                    }
                }
            }
            return c;
        }
    }
}
