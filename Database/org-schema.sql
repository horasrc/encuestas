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
   ===================================================================== */

CREATE TABLE dbo.OrgRole (
    RoleId     VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgRole PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL,
    SortOrder  INT           NOT NULL CONSTRAINT DF_OrgRole_Sort DEFAULT 0,
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgRole_Active DEFAULT 1
);

CREATE TABLE dbo.OrgSegment (
    SegmentId  VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgSegment PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL,
    SortOrder  INT           NOT NULL CONSTRAINT DF_OrgSegment_Sort DEFAULT 0,
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgSegment_Active DEFAULT 1
);

CREATE TABLE dbo.OrgRegion (
    RegionId   VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgRegion PRIMARY KEY,
    Name       NVARCHAR(100) NOT NULL,
    SortOrder  INT           NOT NULL CONSTRAINT DF_OrgRegion_Sort DEFAULT 0,
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgRegion_Active DEFAULT 1
);

CREATE TABLE dbo.OrgBranch (
    BranchId   VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgBranch PRIMARY KEY,
    Name       NVARCHAR(150) NOT NULL,
    RegionId   VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgBranch_Region REFERENCES dbo.OrgRegion(RegionId),
    SegmentId  VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgBranch_Segment REFERENCES dbo.OrgSegment(SegmentId),
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgBranch_Active DEFAULT 1
);

/* Login = User.Identity.Name (ej. DOMINIO\usuario). Se usa para validar la audiencia. */
CREATE TABLE dbo.OrgUser (
    UserId     VARCHAR(40)   NOT NULL CONSTRAINT PK_OrgUser PRIMARY KEY,
    Login      NVARCHAR(100) NOT NULL CONSTRAINT UQ_OrgUser_Login UNIQUE,
    Name       NVARCHAR(150) NOT NULL,
    Email      NVARCHAR(150) NULL,
    RoleId     VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgUser_Role REFERENCES dbo.OrgRole(RoleId),
    BranchId   VARCHAR(40)   NOT NULL CONSTRAINT FK_OrgUser_Branch REFERENCES dbo.OrgBranch(BranchId),
    IsActive   BIT           NOT NULL CONSTRAINT DF_OrgUser_Active DEFAULT 1
);
CREATE INDEX IX_OrgUser_Branch_Role ON dbo.OrgUser(BranchId, RoleId) WHERE IsActive = 1;
