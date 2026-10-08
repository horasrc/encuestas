/* =====================================================================
   PLANTILLA: catálogos de la organización como VISTAS sobre el portal
   ---------------------------------------------------------------------
   Úsala cuando conozcas las tablas de empleados y sucursales del portal.
   Reemplaza las tablas vacías enc.Org* (creadas con org-schema.sql) por
   vistas que leen directamente las tablas del portal: así la segmentación
   y la audiencia usan los datos reales, sin copiarlos.

   CÓMO USARLA
   1. Busca cada «TODO» y cambia nombres de tablas y columnas por los del
      portal. Deja igual los ALIAS (AS RoleId, AS Name, ...): el módulo
      lee esos nombres.
   2. Si el portal no tiene algún catálogo (por ejemplo, segmentos), deja
      la versión "tabla propia" que viene comentada para ese caso.
   3. Ejecuta el script completo en la base del portal (SSMS, F5).

   IMPORTANTE
   - Los Id se devuelven como texto (VARCHAR(40)) porque así se guardan
     en las reglas de segmentación de cada encuesta.
   - Login debe ser exactamente lo que devuelve User.Identity.Name en el
     portal: DOMINIO\usuario.
   - La cuenta del sitio necesita permiso de lectura sobre las tablas del
     portal que usen las vistas, o un GRANT SELECT sobre las vistas (por
     el encadenamiento de propiedad, si las vistas y las tablas tienen el
     mismo dueño "dbo", basta con el GRANT sobre el esquema enc).
   ===================================================================== */

/* 1) Quitar las tablas vacías (solo si existen como TABLA y están vacías). */
IF OBJECT_ID('enc.OrgUser', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM enc.OrgUser)       DROP TABLE enc.OrgUser;
IF OBJECT_ID('enc.OrgBranch', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM enc.OrgBranch)   DROP TABLE enc.OrgBranch;
IF OBJECT_ID('enc.OrgRole', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM enc.OrgRole)       DROP TABLE enc.OrgRole;
IF OBJECT_ID('enc.OrgRegion', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM enc.OrgRegion)   DROP TABLE enc.OrgRegion;
IF OBJECT_ID('enc.OrgSegment', 'U') IS NOT NULL AND NOT EXISTS (SELECT 1 FROM enc.OrgSegment) DROP TABLE enc.OrgSegment;
IF OBJECT_ID('enc.OrgUser', 'U') IS NOT NULL OR OBJECT_ID('enc.OrgBranch', 'U') IS NOT NULL
   OR OBJECT_ID('enc.OrgRole', 'U') IS NOT NULL OR OBJECT_ID('enc.OrgRegion', 'U') IS NOT NULL
   OR OBJECT_ID('enc.OrgSegment', 'U') IS NOT NULL
   OR OBJECT_ID('enc.OrgUser', 'V') IS NOT NULL
BEGIN
    RAISERROR(N'Alto: las vistas ya existen o alguna tabla enc.Org* tiene datos. No se hizo ningún cambio más.', 16, 1);
    SET NOEXEC ON;
END
GO

/* 2) Puestos / roles  (columnas que lee el módulo: RoleId, Name, SortOrder, IsActive) */
CREATE VIEW enc.OrgRole AS
SELECT CAST(p.IdPuesto AS VARCHAR(40)) AS RoleId,          -- TODO: tabla y columnas de puestos
       p.Nombre                         AS Name,
       0                                AS SortOrder,
       CAST(1 AS BIT)                   AS IsActive
FROM dbo.Puestos p;                                         -- TODO
GO

/* 3) Regiones  (RegionId, Name, SortOrder, IsActive) */
CREATE VIEW enc.OrgRegion AS
SELECT CAST(r.IdRegion AS VARCHAR(40)) AS RegionId,         -- TODO
       r.Nombre                        AS Name,
       0                               AS SortOrder,
       CAST(1 AS BIT)                  AS IsActive
FROM dbo.Regiones r;                                        -- TODO
GO

/* 4) Segmentos  (SegmentId, Name, SortOrder, IsActive) */
CREATE VIEW enc.OrgSegment AS
SELECT CAST(g.IdSegmento AS VARCHAR(40)) AS SegmentId,      -- TODO
       g.Nombre                          AS Name,
       0                                 AS SortOrder,
       CAST(1 AS BIT)                    AS IsActive
FROM dbo.Segmentos g;                                       -- TODO
GO
/* Si el portal NO tiene segmentos, borra la vista de arriba y usa una tabla propia del módulo:
CREATE TABLE enc.OrgSegment (
    SegmentId VARCHAR(40) NOT NULL PRIMARY KEY, Name NVARCHAR(100) NOT NULL,
    SortOrder INT NOT NULL DEFAULT 0, IsActive BIT NOT NULL DEFAULT 1);
INSERT enc.OrgSegment (SegmentId, Name) VALUES ('general', N'General');
   y en la vista de sucursales usa  'general' AS SegmentId                                         */

/* 5) Sucursales  (BranchId, Name, RegionId, SegmentId, IsActive) */
CREATE VIEW enc.OrgBranch AS
SELECT CAST(s.IdSucursal AS VARCHAR(40)) AS BranchId,       -- TODO
       s.Nombre                          AS Name,
       CAST(s.IdRegion AS VARCHAR(40))   AS RegionId,       -- TODO
       CAST(s.IdSegmento AS VARCHAR(40)) AS SegmentId,      -- TODO (o 'general')
       CAST(CASE WHEN s.Activa = 1 THEN 1 ELSE 0 END AS BIT) AS IsActive   -- TODO (o CAST(1 AS BIT))
FROM dbo.Sucursales s;                                      -- TODO
GO

/* 6) Colaboradores  (UserId, Login, Name, Email, RoleId, BranchId, IsActive) */
CREATE VIEW enc.OrgUser AS
SELECT CAST(e.IdEmpleado AS VARCHAR(40)) AS UserId,         -- TODO
       e.UsuarioRed                      AS Login,          -- TODO: DOMINIO\usuario
       e.NombreCompleto                  AS Name,           -- TODO
       e.Correo                          AS Email,          -- TODO (o NULL)
       CAST(e.IdPuesto AS VARCHAR(40))   AS RoleId,         -- TODO
       CAST(e.IdSucursal AS VARCHAR(40)) AS BranchId,       -- TODO
       CAST(CASE WHEN e.Activo = 1 THEN 1 ELSE 0 END AS BIT) AS IsActive   -- TODO
FROM dbo.Empleados e;                                       -- TODO
GO

/* 7) Comprobación: deben salir filas en cada catálogo. */
SELECT 'Roles' AS Catalogo, COUNT(*) AS Filas FROM enc.OrgRole
UNION ALL SELECT 'Regiones', COUNT(*) FROM enc.OrgRegion
UNION ALL SELECT 'Segmentos', COUNT(*) FROM enc.OrgSegment
UNION ALL SELECT 'Sucursales', COUNT(*) FROM enc.OrgBranch
UNION ALL SELECT 'Colaboradores', COUNT(*) FROM enc.OrgUser;
GO
SET NOEXEC OFF;
GO
