/* =====================================================================
   UPGRADE 001 - Modo de segmentación por versión
   Solo para bases creadas con una versión anterior de schema.sql
   (el schema.sql actual ya incluye la columna). Se puede ejecutar varias veces.
   ===================================================================== */
IF COL_LENGTH('dbo.SurveyVersion', 'AudienceMode') IS NULL
BEGIN
    ALTER TABLE dbo.SurveyVersion
        ADD AudienceMode TINYINT NOT NULL CONSTRAINT DF_SV_AudienceMode DEFAULT 0; -- 0 todos, 1 segmentada
END
GO

/* Las versiones que ya tenían filtros de audiencia quedan como segmentadas. */
UPDATE v SET AudienceMode = 1
FROM dbo.SurveyVersion v
WHERE v.AudienceMode = 0
  AND EXISTS (SELECT 1 FROM dbo.SurveyAudienceRule a WHERE a.SurveyVersionId = v.SurveyVersionId);
GO
