/* =====================================================================
   MÓDULO DE ENCUESTAS DINÁMICAS - ESQUEMA SQL SERVER
   ---------------------------------------------------------------------
   Principios:
   - El contenido se versiona: Survey -> SurveyVersion -> Question.
     Una versión publicada con respuestas es inmutable (lo controla
     SurveyService y, como red de seguridad, el trigger del final).
   - Las respuestas son dinámicas (modelo EAV): una fila por valor
     respondido en QuestionResponse. Nunca columnas pregunta1, pregunta2...
   - Las validaciones específicas por tipo viven en Question.SettingsJson
     para agregar tipos nuevos sin alterar tablas.
   - Todas las tablas viven en el esquema "enc": se pueden crear dentro de
     la base del portal sin chocar con sus tablas (dbo.*), y los permisos se
     dan sobre el esquema completo (GRANT ... ON SCHEMA::enc).
   ===================================================================== */

/* Si ya está instalado, no hace nada (SET NOEXEC ON salta el resto del script). */
IF OBJECT_ID('enc.Survey', 'U') IS NOT NULL
BEGIN
    RAISERROR(N'Las tablas del módulo de encuestas ya existen (enc.Survey). No se ejecutó nada. Para actualizar una instalación existente usa los scripts upgrade-*.sql.', 16, 1);
    SET NOEXEC ON;
END
GO

/* Si el esquema ya tiene otras tablas (uno compartido con el portal), ninguna debe llamarse igual que las del módulo. */
DECLARE @choque NVARCHAR(400) = (
    SELECT STUFF((SELECT N', ' + o.name FROM sys.objects o
                  WHERE o.schema_id = SCHEMA_ID('enc') AND o.name IN (N'Survey', N'SurveyVersion', N'SurveyAudienceRule', N'QuestionType',
                        N'Question', N'QuestionOption', N'DependentLevel', N'DependentOption', N'ConditionalRule', N'SurveyResponse',
                        N'QuestionResponse', N'SurveyAuditLog', N'TR_Question_Immutable')
                  FOR XML PATH('')), 1, 2, N''));
IF @choque IS NOT NULL
BEGIN
    RAISERROR(N'El esquema enc ya tiene objetos con el mismo nombre que el módulo: %s. No se ejecutó nada.', 16, 1, @choque);
    SET NOEXEC ON;
END
GO

IF SCHEMA_ID('enc') IS NULL EXEC('CREATE SCHEMA enc AUTHORIZATION dbo');
GO

CREATE TABLE enc.Survey (
    SurveyId        INT IDENTITY(1,1)  NOT NULL CONSTRAINT PK_Survey PRIMARY KEY,
    Name            NVARCHAR(200)      NOT NULL,
    Description     NVARCHAR(1000)     NULL,
    CreatedBy       NVARCHAR(100)      NOT NULL,
    CreatedAt       DATETIME2(0)       NOT NULL CONSTRAINT DF_Survey_CreatedAt DEFAULT SYSDATETIME(),
    IsDeleted       BIT                NOT NULL CONSTRAINT DF_Survey_IsDeleted DEFAULT 0
);

/* Status: 1 Borrador, 2 Publicada, 3 Cerrada, 4 Archivada */
CREATE TABLE enc.SurveyVersion (
    SurveyVersionId         INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SurveyVersion PRIMARY KEY,
    SurveyId                INT            NOT NULL CONSTRAINT FK_SurveyVersion_Survey REFERENCES enc.Survey(SurveyId),
    VersionNumber           INT            NOT NULL,
    Status                  TINYINT        NOT NULL CONSTRAINT CK_SurveyVersion_Status CHECK (Status BETWEEN 1 AND 4),
    StartDate               DATE           NULL,
    EndDate                 DATE           NULL,
    AllowMultipleResponses  BIT            NOT NULL CONSTRAINT DF_SV_Multiple DEFAULT 0,
    IsAnonymous             BIT            NOT NULL CONSTRAINT DF_SV_Anonymous DEFAULT 0,
    RequiresAuthentication  BIT            NOT NULL CONSTRAINT DF_SV_Auth DEFAULT 1,
    WelcomeMessage          NVARCHAR(2000) NULL,
    ThankYouMessage         NVARCHAR(2000) NULL,
    AudienceMode            TINYINT        NOT NULL CONSTRAINT DF_SV_AudienceMode DEFAULT 0, -- 0 todos, 1 segmentada (filtros en SurveyAudienceRule)
    ResponseCount          INT            NOT NULL CONSTRAINT DF_SV_ResponseCount DEFAULT 0,
    CreatedAt               DATETIME2(0)   NOT NULL CONSTRAINT DF_SV_CreatedAt DEFAULT SYSDATETIME(),
    CreatedBy               NVARCHAR(100)  NOT NULL,
    PublishedAt             DATETIME2(0)   NULL,
    ClosedAt                DATETIME2(0)   NULL,
    CONSTRAINT UQ_SurveyVersion UNIQUE (SurveyId, VersionNumber),
    CONSTRAINT CK_SurveyVersion_Dates CHECK (EndDate IS NULL OR StartDate IS NULL OR EndDate >= StartDate)
);

/* Solo un borrador y una versión publicada por encuesta. */
CREATE UNIQUE INDEX UX_SurveyVersion_OneDraft     ON enc.SurveyVersion(SurveyId) WHERE Status = 1;
CREATE UNIQUE INDEX UX_SurveyVersion_OnePublished ON enc.SurveyVersion(SurveyId) WHERE Status = 2;

/* Segmentación: Dimension = Role | Segment | Region | Branch | User */
CREATE TABLE enc.SurveyAudienceRule (
    AudienceRuleId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SurveyAudienceRule PRIMARY KEY,
    SurveyVersionId  INT           NOT NULL CONSTRAINT FK_Audience_Version REFERENCES enc.SurveyVersion(SurveyVersionId) ON DELETE CASCADE,
    Dimension        VARCHAR(20)   NOT NULL CONSTRAINT CK_Audience_Dimension CHECK (Dimension IN ('Role','Segment','Region','Branch','User')),
    Value            NVARCHAR(100) NOT NULL
);
CREATE INDEX IX_Audience_Version ON enc.SurveyAudienceRule(SurveyVersionId, Dimension);

/* Catálogo de tipos de pregunta */
CREATE TABLE enc.QuestionType (
    QuestionTypeId  INT          NOT NULL CONSTRAINT PK_QuestionType PRIMARY KEY,
    Code            VARCHAR(30)  NOT NULL CONSTRAINT UQ_QuestionType_Code UNIQUE,
    Name            NVARCHAR(60) NOT NULL,
    Icon            VARCHAR(40)  NULL
);

INSERT INTO enc.QuestionType (QuestionTypeId, Code, Name, Icon) VALUES
 (1,  'singleChoice',   N'Opción única',               'bi-ui-radios'),
 (2,  'multipleChoice', N'Selección múltiple',         'bi-ui-checks'),
 (3,  'text',           N'Texto libre',                'bi-textarea-t'),
 (4,  'yesNo',          N'Sí / No',                    'bi-toggle-on'),
 (5,  'scale',          N'Escala',                     'bi-sliders'),
 (6,  'date',           N'Fecha',                      'bi-calendar-event'),
 (7,  'number',         N'Número',                     'bi-123'),
 (8,  'dropdown',       N'Desplegable',                'bi-menu-button-wide'),
 (9,  'matrix',         N'Matriz',                     'bi-grid-3x3'),
 (10, 'dependent',      N'Multiselección dependiente', 'bi-diagram-3');

CREATE TABLE enc.Question (
    QuestionId        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Question PRIMARY KEY,
    SurveyVersionId   INT            NOT NULL CONSTRAINT FK_Question_Version REFERENCES enc.SurveyVersion(SurveyVersionId) ON DELETE CASCADE,
    QuestionTypeId    INT            NOT NULL CONSTRAINT FK_Question_Type REFERENCES enc.QuestionType(QuestionTypeId),
    ClientKey         VARCHAR(40)    NOT NULL,           -- id usado por el front-end ("q_k3j2")
    SortOrder         INT            NOT NULL,
    Text              NVARCHAR(1000) NOT NULL,
    HelpText          NVARCHAR(1000) NULL,
    IsRequired        BIT            NOT NULL CONSTRAINT DF_Question_Required DEFAULT 0,
    RandomizeOptions  BIT            NOT NULL CONSTRAINT DF_Question_Random DEFAULT 0,
    AllowOther        BIT            NOT NULL CONSTRAINT DF_Question_Other DEFAULT 0,
    OtherLabel        NVARCHAR(100)  NULL,
    SettingsJson      NVARCHAR(MAX)  NULL,               -- QuestionValidationDto
    CONSTRAINT UQ_Question_ClientKey UNIQUE (SurveyVersionId, ClientKey)
);
CREATE INDEX IX_Question_Version ON enc.Question(SurveyVersionId, SortOrder);

/* Role: 1 opción, 2 fila de matriz, 3 columna de matriz */
CREATE TABLE enc.QuestionOption (
    QuestionOptionId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuestionOption PRIMARY KEY,
    QuestionId        INT           NOT NULL CONSTRAINT FK_Option_Question REFERENCES enc.Question(QuestionId) ON DELETE CASCADE,
    Role              TINYINT       NOT NULL CONSTRAINT DF_Option_Role DEFAULT 1,
    ClientKey         VARCHAR(40)   NOT NULL,
    Text              NVARCHAR(500) NOT NULL,
    Value             NVARCHAR(100) NULL,
    SortOrder         INT           NOT NULL
);
CREATE INDEX IX_Option_Question ON enc.QuestionOption(QuestionId, Role, SortOrder);

/* Listas encadenadas */
CREATE TABLE enc.DependentLevel (
    DependentLevelId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DependentLevel PRIMARY KEY,
    QuestionId        INT           NOT NULL CONSTRAINT FK_DepLevel_Question REFERENCES enc.Question(QuestionId) ON DELETE CASCADE,
    LevelIndex        INT           NOT NULL,
    Name              NVARCHAR(100) NOT NULL,
    CONSTRAINT UQ_DependentLevel UNIQUE (QuestionId, LevelIndex)
);

CREATE TABLE enc.DependentOption (
    DependentOptionId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_DependentOption PRIMARY KEY,
    QuestionId         INT           NOT NULL CONSTRAINT FK_DepOption_Question REFERENCES enc.Question(QuestionId) ON DELETE CASCADE,
    ParentOptionId     INT           NULL CONSTRAINT FK_DepOption_Parent REFERENCES enc.DependentOption(DependentOptionId),
    LevelIndex         INT           NOT NULL,
    ClientKey          VARCHAR(40)   NOT NULL,
    Text               NVARCHAR(300) NOT NULL,
    SortOrder          INT           NOT NULL
);
CREATE INDEX IX_DepOption_Parent ON enc.DependentOption(QuestionId, ParentOptionId, SortOrder);

/* Lógica condicional: SI respuesta [Operator] [CompareValue] ENTONCES [Action] [TargetQuestionId] */
CREATE TABLE enc.ConditionalRule (
    ConditionalRuleId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ConditionalRule PRIMARY KEY,
    QuestionId         INT           NOT NULL CONSTRAINT FK_Rule_Question REFERENCES enc.Question(QuestionId) ON DELETE CASCADE,
    Operator           VARCHAR(20)   NOT NULL CONSTRAINT CK_Rule_Operator CHECK (Operator IN ('equals','notEquals','gt','lt','contains','answered')),
    CompareValue       NVARCHAR(200) NULL,       -- ClientKey de opción, número o texto
    Action             VARCHAR(10)   NOT NULL CONSTRAINT CK_Rule_Action CHECK (Action IN ('show','hide','goto','end')),
    TargetQuestionId   INT           NULL CONSTRAINT FK_Rule_Target REFERENCES enc.Question(QuestionId),
    SortOrder          INT           NOT NULL
);
CREATE INDEX IX_Rule_Question ON enc.ConditionalRule(QuestionId);

/* Respuestas */
CREATE TABLE enc.SurveyResponse (
    SurveyResponseId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SurveyResponse PRIMARY KEY,
    SurveyVersionId   INT           NOT NULL CONSTRAINT FK_Response_Version REFERENCES enc.SurveyVersion(SurveyVersionId),
    UserId            NVARCHAR(100) NULL,        -- NULL si la encuesta es anónima
    UserHash          CHAR(64)      NULL,        -- SHA-256 del usuario: controla "una respuesta por usuario" sin exponer identidad
    StartedAt         DATETIME2(0)  NOT NULL,
    SubmittedAt       DATETIME2(0)  NULL
);
CREATE INDEX IX_Response_Version ON enc.SurveyResponse(SurveyVersionId, SubmittedAt);
CREATE INDEX IX_Response_UserHash ON enc.SurveyResponse(SurveyVersionId, UserHash);

CREATE TABLE enc.QuestionResponse (
    QuestionResponseId  BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_QuestionResponse PRIMARY KEY,
    SurveyResponseId    BIGINT         NOT NULL CONSTRAINT FK_QR_Response REFERENCES enc.SurveyResponse(SurveyResponseId) ON DELETE CASCADE,
    QuestionId          INT            NOT NULL CONSTRAINT FK_QR_Question REFERENCES enc.Question(QuestionId),
    QuestionOptionId    INT            NULL CONSTRAINT FK_QR_Option REFERENCES enc.QuestionOption(QuestionOptionId),
    MatrixRowOptionId   INT            NULL CONSTRAINT FK_QR_MatrixRow REFERENCES enc.QuestionOption(QuestionOptionId),
    DependentOptionId   INT            NULL CONSTRAINT FK_QR_DepOption REFERENCES enc.DependentOption(DependentOptionId),
    TextValue           NVARCHAR(MAX)  NULL,
    NumberValue         DECIMAL(18,4)  NULL,
    DateValue           DATE           NULL
);
CREATE INDEX IX_QR_Question ON enc.QuestionResponse(QuestionId) INCLUDE (QuestionOptionId, MatrixRowOptionId, DependentOptionId, NumberValue);
CREATE INDEX IX_QR_Response ON enc.QuestionResponse(SurveyResponseId);

/* Bitácora: quién hizo qué y cuándo (no tiene FK a Survey para conservarse aunque la encuesta se elimine). */
CREATE TABLE enc.SurveyAuditLog (
    AuditId          BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_SurveyAuditLog PRIMARY KEY,
    SurveyId         INT            NOT NULL,
    VersionNumber    INT            NULL,
    Action           VARCHAR(30)    NOT NULL,   -- created, saved, newVersion, published, archived, closed, duplicated, deleted
    Detail           NVARCHAR(500)  NULL,
    UserName         NVARCHAR(100)  NOT NULL,
    CreatedAt        DATETIME2(0)   NOT NULL CONSTRAINT DF_Audit_CreatedAt DEFAULT SYSDATETIME()
);
CREATE INDEX IX_Audit_Survey ON enc.SurveyAuditLog(SurveyId, CreatedAt DESC);
GO

/* ---------------------------------------------------------------------
   Red de seguridad del versionado: impide modificar preguntas de una
   versión publicada que ya tiene respuestas.
   --------------------------------------------------------------------- */
CREATE TRIGGER enc.TR_Question_Immutable ON enc.Question
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1
        FROM (SELECT SurveyVersionId FROM inserted UNION SELECT SurveyVersionId FROM deleted) x
        JOIN enc.SurveyVersion v ON v.SurveyVersionId = x.SurveyVersionId
        WHERE v.ResponseCount > 0 AND v.Status <> 1
    )
    BEGIN
        RAISERROR (N'La versión tiene respuestas y no puede modificarse. Cree una nueva versión.', 16, 1);
        ROLLBACK TRANSACTION;
    END
END
GO

/* ---------------------------------------------------------------------
   Ejemplo de consulta de resultados: conteo por opción (opción única /
   selección múltiple) para una versión.
   --------------------------------------------------------------------- */
-- SELECT q.QuestionId, q.Text, o.Text AS Opcion, COUNT(qr.QuestionResponseId) AS Conteo
-- FROM enc.Question q
-- JOIN enc.QuestionOption o ON o.QuestionId = q.QuestionId AND o.Role = 1
-- LEFT JOIN enc.QuestionResponse qr ON qr.QuestionOptionId = o.QuestionOptionId AND qr.MatrixRowOptionId IS NULL
-- WHERE q.SurveyVersionId = @SurveyVersionId
-- GROUP BY q.QuestionId, q.SortOrder, q.Text, o.SortOrder, o.Text
-- ORDER BY q.SortOrder, o.SortOrder;

SET NOEXEC OFF;
GO
DECLARE @n INT = (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID('enc') AND name IN (N'Survey', N'SurveyVersion',
    N'SurveyAudienceRule', N'QuestionType', N'Question', N'QuestionOption', N'DependentLevel', N'DependentOption', N'ConditionalRule',
    N'SurveyResponse', N'QuestionResponse', N'SurveyAuditLog'));
PRINT N'Esquema enc: ' + CAST(@n AS NVARCHAR(10)) + N' tablas del módulo.';
GO
