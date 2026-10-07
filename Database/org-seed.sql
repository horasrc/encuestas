/* =====================================================================
   DATOS DE EJEMPLO para los catálogos organizacionales (org-schema.sql).
   Son los mismos catálogos de la demo (mock-data.js). En producción estas
   tablas las llena RH / ETL; NO ejecutar este script en producción.

   Además de colaboradores generados, agrega al usuario que ejecuta el
   script (SUSER_SNAME(), ej. DOMINIO\tuusuario) como Gerente de "Reforma",
   para poder probar Surveys:EnforceAudience con tu propia sesión.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

INSERT INTO dbo.OrgRole (RoleId, Name, SortOrder) VALUES
 ('ejecutivos', N'Ejecutivos', 1), ('gerentes', N'Gerentes', 2), ('subdirectores', N'Subdirectores', 3), ('asesores', N'Asesores digitales', 4);

INSERT INTO dbo.OrgSegment (SegmentId, Name, SortOrder) VALUES
 ('bp', N'Banca Personal', 1), ('premier', N'Premier', 2), ('rbb', N'RBB Digital', 3), ('pyme', N'PyME', 4);

INSERT INTO dbo.OrgRegion (RegionId, Name, SortOrder) VALUES
 ('norte', N'Norte', 1), ('centro', N'Centro', 2), ('metro', N'Metropolitana', 3), ('occidente', N'Occidente', 4), ('sur', N'Sur', 5);

/* Sucursales con su plantilla por rol (solo para generar colaboradores de ejemplo). */
DECLARE @Branches TABLE (BranchId VARCHAR(40), Name NVARCHAR(150), RegionId VARCHAR(40), SegmentId VARCHAR(40),
                         Ej INT, Ge INT, Sub INT, Ase INT);
INSERT INTO @Branches VALUES
 ('b01', N'Monterrey Centro', 'norte', 'bp', 24, 3, 1, 4),
 ('b02', N'San Pedro', 'norte', 'premier', 16, 2, 1, 2),
 ('b03', N'Chihuahua', 'norte', 'bp', 18, 2, 0, 3),
 ('b04', N'Reforma', 'metro', 'premier', 22, 3, 1, 4),
 ('b05', N'Polanco', 'metro', 'premier', 20, 3, 1, 3),
 ('b06', N'Insurgentes', 'metro', 'bp', 26, 3, 1, 5),
 ('b07', N'Querétaro', 'centro', 'bp', 17, 2, 0, 3),
 ('b08', N'Puebla', 'centro', 'rbb', 12, 2, 0, 8),
 ('b09', N'Guadalajara Chapultepec', 'occidente', 'bp', 25, 3, 1, 4),
 ('b10', N'Zapopan', 'occidente', 'premier', 15, 2, 1, 2),
 ('b11', N'Mérida', 'sur', 'bp', 14, 2, 0, 3),
 ('b12', N'Cancún', 'sur', 'pyme', 10, 2, 0, 2),
 ('b13', N'Centro Digital CDMX', 'metro', 'rbb', 6, 2, 1, 20),
 ('b14', N'León', 'centro', 'pyme', 11, 2, 0, 2);

INSERT INTO dbo.OrgBranch (BranchId, Name, RegionId, SegmentId)
SELECT BranchId, Name, RegionId, SegmentId FROM @Branches;

/* Colaboradores generados: DEMO\b01-ejecutivos-001, ... */
;WITH Numbers AS (
    SELECT TOP (100) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS N FROM sys.all_objects
),
Plantilla AS (
    SELECT BranchId, 'ejecutivos' AS RoleId, Ej AS Total FROM @Branches
    UNION ALL SELECT BranchId, 'gerentes', Ge FROM @Branches
    UNION ALL SELECT BranchId, 'subdirectores', Sub FROM @Branches
    UNION ALL SELECT BranchId, 'asesores', Ase FROM @Branches
)
INSERT INTO dbo.OrgUser (UserId, Login, Name, Email, RoleId, BranchId)
SELECT
    p.BranchId + '-' + LEFT(p.RoleId, 3) + '-' + RIGHT('00' + CAST(n.N AS VARCHAR(3)), 3),
    N'DEMO\' + p.BranchId + '-' + p.RoleId + '-' + RIGHT('00' + CAST(n.N AS VARCHAR(3)), 3),
    N'Colaborador ' + UPPER(p.BranchId) + ' ' + r.Name + ' ' + CAST(n.N AS NVARCHAR(3)),
    p.BranchId + '.' + p.RoleId + CAST(n.N AS VARCHAR(3)) + '@empresa.com',
    p.RoleId, p.BranchId
FROM Plantilla p
JOIN Numbers n ON n.N <= p.Total
JOIN dbo.OrgRole r ON r.RoleId = p.RoleId;

/* Tu usuario (quien ejecuta el script) como Gerente de Reforma. */
IF NOT EXISTS (SELECT 1 FROM dbo.OrgUser WHERE Login = SUSER_SNAME())
    INSERT INTO dbo.OrgUser (UserId, Login, Name, Email, RoleId, BranchId)
    VALUES ('yo', SUSER_SNAME(), N'Mi usuario (pruebas)', NULL, 'gerentes', 'b04');

COMMIT TRANSACTION;

SELECT COUNT(*) AS Colaboradores FROM dbo.OrgUser;
