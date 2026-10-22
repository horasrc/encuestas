-- GENERADO por tools\esquema-sql.ps1 para el esquema [IC] a partir de Database\upgrade-001-audience-mode.sql. No editar a mano.
-- SIN LLAVES FORÁNEAS: para instalar sin el permiso REFERENCES. La integridad entre tablas la mantiene el módulo.
/* =====================================================================
   UPGRADE 001 - Modo de segmentación por versión
   Solo para bases creadas con una versión anterior de schema.sql
   (el schema.sql actual ya incluye la columna). Se puede ejecutar varias veces.
   ===================================================================== */
IF COL_LENGTH('IC.SurveyVersion', 'AudienceMode') IS NULL
BEGIN
    ALTER TABLE IC.SurveyVersion
        ADD AudienceMode TINYINT NOT NULL CONSTRAINT DF_SV_AudienceMode DEFAULT 0; -- 0 todos, 1 segmentada
END
GO

/* Las versiones que ya tenían filtros de audiencia quedan como segmentadas. */
UPDATE v SET AudienceMode = 1
FROM IC.SurveyVersion v
WHERE v.AudienceMode = 0
  AND EXISTS (SELECT 1 FROM IC.SurveyAudienceRule a WHERE a.SurveyVersionId = v.SurveyVersionId);
GO
