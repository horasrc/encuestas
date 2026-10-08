/* =====================================================================
   UPGRADE 002 - Bitácora de cambios (SurveyAuditLog)
   Para bases creadas antes de esta versión (el schema.sql actual ya la incluye).
   Se puede ejecutar varias veces.
   ===================================================================== */
IF OBJECT_ID('enc.SurveyAuditLog') IS NULL
BEGIN
    CREATE TABLE enc.SurveyAuditLog (
        AuditId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SurveyAuditLog PRIMARY KEY,
        SurveyId         INT            NOT NULL,
        VersionNumber    INT            NULL,
        Action           VARCHAR(30)    NOT NULL,
        Detail           NVARCHAR(500)  NULL,
        UserName         NVARCHAR(100)  NOT NULL,
        CreatedAt        DATETIME2(0)   NOT NULL CONSTRAINT DF_Audit_CreatedAt DEFAULT SYSDATETIME()
    );
    CREATE INDEX IX_Audit_Survey ON enc.SurveyAuditLog(SurveyId, CreatedAt DESC);
END
GO
