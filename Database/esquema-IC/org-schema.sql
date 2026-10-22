-- GENERADO por tools\esquema-sql.ps1 para el esquema [IC] a partir de Database\org-schema.sql. No editar a mano.
/* =====================================================================
   CATÁLOGOS ORGANIZACIONALES (segmentación de encuestas)
   ---------------------------------------------------------------------
   Los lee SqlOrgCatalogProvider (Web.config -> Surveys:CatalogProvider = Sql).
   Lo normal es que un proceso de RH / ETL / sincronización con Active
   Directory las mantenga actualizadas; el módulo solo las LEE.

   Si tu organización ya tiene estas tablas con otros nombres, en lugar de
   crearlas puedes definir VISTAS con estos nombres y columnas.

   Los IDs son texto (VARCHAR) porque se guardan tal cual en
   SurveyAudienceRule.Value.

   Viven en el esquema "IC" junto con el resto del módulo. Si el portal ya
   tiene empleados, sucursales, etc., lo recomendable es NO ejecutar este
   script y crear en su lugar VISTAS IC.OrgRole, IC.OrgUser, ... sobre las
   tablas del portal (ver INTEGRACION.md).
   ===================================================================== */

/* Si ya existen (como tablas o como vistas sobre el portal), no hace nada. */
IF OBJECT_ID('IC.OrgUser') IS NOT NULL
BEGIN
    RAISERROR(N'IC.OrgUser ya existe (tabla o vista). No se ejecutó nada.', 16, 1);
    SET NOEXEC ON;
END
GO

/* En un esquema compartido con el portal, ningún objeto debe llamarse igual que estas tablas. */
IF EXISTS (SELECT 1 FROM sys.objects WHERE schema_id = SCHEMA_ID('IC') AND name IN (N'OrgRole', N'OrgSegment', N'OrgRegion', N'OrgBranch'))
BEGIN
    RAISERROR(N'El esquema IC ya tiene un objeto OrgRole, OrgSegment, OrgRegion u OrgBranch. No se ejecutó nada.', 16, 1);
    SET NOEXEC ON;
END
GO

IF SCHEMA_ID('IC') IS NULL EXEC('CREATE SCHEMA IC AUTHORIZATION dbo');
GO

CREATE TABLE IC.OrgRole (
    RoleId     VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgRole PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL,
    SortOrder  INT           NOT NULL CONSTRAINT DF_OrgRole_Sort DEFAULT 0,
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgRole_Active DEFAULT 1
);

CREATE TABLE IC.OrgSegment (
    SegmentId  VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgSegment PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL,
    SortOrder  INT           NOT NULL CONSTRAINT DF_OrgSegment_Sort DEFAULT 0,
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgSegment_Active DEFAULT 1
);

CREATE TABLE IC.OrgRegion (
    RegionId   VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgRegion PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL,
    SortOrder  INT           NOT NULL CONSTRAINT DF_OrgRegion_Sort DEFAULT 0,
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgRegion_Active DEFAULT 1
);

CREATE TABLE IC.OrgBranch (
    BranchId   VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgBranch PRIMARY KEY,
    Name       NVARCHAR(150) NOT NULL,
    RegionId   VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgBranch_Region REFERENCES IC.OrgRegion(RegionId),
    SegmentId  VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgBranch_Segment REFERENCES IC.OrgSegment(SegmentId),
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgBranch_Active DEFAULT 1
);

/* Login = User.Identity.Name (ej. DOMINIO\usuario). Se usa para validar la audiencia. */
CREATE TABLE IC.OrgUser (
    UserId     VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgUser PRIMARY KEY,
    Login      NVARCHAR(100) NOT NULL CONSTRAINT UQ_OrgUser_Login UNIQUE,
    Name       NVARCHAR(150) NOT NULL,
    Email      NVARCHAR(150) NULL,
    RoleId     VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgUser_Role REFERENCES IC.OrgRole(RoleId),
    BranchId   VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgUser_Branch REFERENCES IC.OrgBranch(BranchId),
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgUser_Active DEFAULT 1
);
CREATE INDEX IX_OrgUser_Branch_Role ON IC.OrgUser(BranchId, RoleId) WHERE IsActive = 1;
GO

SET NOEXEC OFF;
GO
