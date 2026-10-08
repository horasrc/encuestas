/* =====================================================================
   UPGRADE 003 - Mover las tablas del módulo del esquema dbo al esquema enc
   Solo para bases de DESARROLLO creadas antes de este cambio (cuando las
   tablas estaban en dbo). Una instalación nueva ya las crea en enc.
   Mueve tablas con sus datos, llaves, índices y el trigger. Re-ejecutable.
   ===================================================================== */
IF SCHEMA_ID('enc') IS NULL EXEC('CREATE SCHEMA enc AUTHORIZATION dbo');
GO

DECLARE @t SYSNAME, @sql NVARCHAR(400);
DECLARE tablas CURSOR LOCAL FAST_FORWARD FOR
    SELECT name FROM (VALUES
        ('Survey'), ('SurveyVersion'), ('SurveyAudienceRule'), ('QuestionType'), ('Question'),
        ('QuestionOption'), ('DependentLevel'), ('DependentOption'), ('ConditionalRule'),
        ('SurveyResponse'), ('QuestionResponse'), ('SurveyAuditLog'),
        ('OrgRole'), ('OrgSegment'), ('OrgRegion'), ('OrgBranch'), ('OrgUser')) AS x(name)
    WHERE OBJECT_ID('dbo.' + name, 'U') IS NOT NULL AND OBJECT_ID('enc.' + name, 'U') IS NULL;
OPEN tablas;
FETCH NEXT FROM tablas INTO @t;
WHILE @@FETCH_STATUS = 0
BEGIN
    SET @sql = N'ALTER SCHEMA enc TRANSFER dbo.' + QUOTENAME(@t) + N';';
    EXEC (@sql);
    PRINT 'Movida: dbo.' + @t + ' -> enc.' + @t;
    FETCH NEXT FROM tablas INTO @t;
END
CLOSE tablas; DEALLOCATE tablas;
GO
