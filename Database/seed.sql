/* =====================================================================
   DATOS SEMILLA - encuesta de ejemplo para SqlSurveyRepository
   Ejecutar después de schema.sql. Crea la encuesta en BORRADOR (v1) con:
   opción única, selección múltiple con "Otro", texto libre con límite,
   pregunta condicional, multiselección dependiente, desplegable y matriz.
   Al final imprime el SurveyId: ponerlo en Web.config -> Surveys:DefaultSurveyId.

   Los ClientKey (q1, q1o1, ...) son los IDs que usa el front-end.
   SettingsJson usa el formato de SqlSurveyRepository.StoredSettings.
   ===================================================================== */
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @SurveyId INT, @V INT, @Q INT, @Q4 INT, @Q5 INT, @Q6 INT, @P1 INT, @P2 INT;

INSERT INTO dbo.Survey (Name, Description, CreatedBy)
VALUES (N'Experiencia con campañas comerciales', N'Percepción de la fuerza comercial sobre el material de la campaña.', N'seed');
SET @SurveyId = SCOPE_IDENTITY();

INSERT INTO dbo.SurveyVersion (SurveyId, VersionNumber, Status, StartDate, EndDate, AllowMultipleResponses, IsAnonymous,
    RequiresAuthentication, WelcomeMessage, ThankYouMessage, AudienceMode, CreatedBy)
VALUES (@SurveyId, 1, 1, CAST(GETDATE() AS DATE), DATEADD(DAY, 30, CAST(GETDATE() AS DATE)), 0, 1, 1,
    N'Queremos conocer tu experiencia con el material de la campaña. Te tomará menos de 5 minutos.',
    N'¡Gracias por tu respuesta!', 1, N'seed');
SET @V = SCOPE_IDENTITY();

/* 1. Opción única */
INSERT INTO dbo.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, IsRequired, SettingsJson)
VALUES (@V, 1, 'q1', 1, N'¿Qué tan útil fue el material de la campaña?', 1, N'{"Validation":{}}');
SET @Q = SCOPE_IDENTITY();
INSERT INTO dbo.QuestionOption (QuestionId, Role, ClientKey, Text, SortOrder) VALUES
 (@Q, 1, 'q1o1', N'Muy útil', 1), (@Q, 1, 'q1o2', N'Útil', 2), (@Q, 1, 'q1o3', N'Neutral', 3),
 (@Q, 1, 'q1o4', N'Poco útil', 4), (@Q, 1, 'q1o5', N'Nada útil', 5);

/* 2. Selección múltiple con "Otro" (1 a 4 selecciones) */
INSERT INTO dbo.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, HelpText, IsRequired, AllowOther, OtherLabel, SettingsJson)
VALUES (@V, 2, 'q2', 2, N'¿Qué materiales utilizaste en esta campaña?', N'Puedes elegir varias opciones.', 1, 1, N'Otro (especifica)',
    N'{"Validation":{"MinSelections":1,"MaxSelections":4}}');
SET @Q = SCOPE_IDENTITY();
INSERT INTO dbo.QuestionOption (QuestionId, Role, ClientKey, Text, SortOrder) VALUES
 (@Q, 1, 'q2o1', N'Guía comercial', 1), (@Q, 1, 'q2o2', N'Argumentos de venta', 2), (@Q, 1, 'q2o3', N'Presentación', 3),
 (@Q, 1, 'q2o4', N'Videos', 4), (@Q, 1, 'q2o5', N'Correos electrónicos', 5);

/* 3. Texto libre (10 a 500 caracteres) */
INSERT INTO dbo.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, SettingsJson)
VALUES (@V, 3, 'q3', 3, N'¿Qué mejorarías en esta campaña?', N'{"Validation":{"TextMode":"long","MinLength":10,"MaxLength":500}}');

/* 4. Condicional */
INSERT INTO dbo.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, IsRequired, SettingsJson)
VALUES (@V, 1, 'q4', 4, N'¿Actualmente utilizas Productivity Suite?', 1, N'{"Validation":{}}');
SET @Q4 = SCOPE_IDENTITY();
INSERT INTO dbo.QuestionOption (QuestionId, Role, ClientKey, Text, SortOrder) VALUES
 (@Q4, 1, 'q4o1', N'Sí', 1), (@Q4, 1, 'q4o2', N'No', 2), (@Q4, 1, 'q4o3', N'No aplica', 3);

/* 5. Multiselección dependiente: Banca > Producto > Recurso */
INSERT INTO dbo.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, HelpText, IsRequired, SettingsJson)
VALUES (@V, 10, 'q5', 5, N'¿Qué recursos de Productivity Suite consultas?', N'Marca la banca, después el producto y por último el recurso.', 1,
    N'{"Validation":{},"DependentMultiple":true}');
SET @Q5 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentLevel (QuestionId, LevelIndex, Name) VALUES (@Q5, 0, N'Banca'), (@Q5, 1, N'Producto'), (@Q5, 2, N'Recurso');

INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES (@Q5, NULL, 0, 'q5d1', N'Banca Personal', 1);
SET @P1 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES (@Q5, @P1, 1, 'q5d2', N'Tarjeta de crédito', 2);
SET @P2 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES
 (@Q5, @P2, 2, 'q5d3', N'Guía comercial', 3), (@Q5, @P2, 2, 'q5d4', N'Argumentos de venta', 4),
 (@Q5, @P2, 2, 'q5d5', N'Manejo de objeciones', 5), (@Q5, @P2, 2, 'q5d6', N'Presentación', 6);
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES (@Q5, @P1, 1, 'q5d7', N'Portabilidad', 7);
SET @P2 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES
 (@Q5, @P2, 2, 'q5d8', N'Argumentos de venta', 8), (@Q5, @P2, 2, 'q5d9', N'Manejo de objeciones', 9);
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES (@Q5, NULL, 0, 'q5d10', N'Premier', 10);
SET @P1 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES (@Q5, @P1, 1, 'q5d11', N'Inversiones', 11);
SET @P2 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES
 (@Q5, @P2, 2, 'q5d12', N'Guía comercial', 12), (@Q5, @P2, 2, 'q5d13', N'Presentación', 13);
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES (@Q5, NULL, 0, 'q5d14', N'RBB Digital', 14);
SET @P1 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES (@Q5, @P1, 1, 'q5d15', N'Banca móvil', 15);
SET @P2 = SCOPE_IDENTITY();
INSERT INTO dbo.DependentOption (QuestionId, ParentOptionId, LevelIndex, ClientKey, Text, SortOrder) VALUES
 (@Q5, @P2, 2, 'q5d16', N'Guía comercial', 16), (@Q5, @P2, 2, 'q5d17', N'Tutorial en video', 17);

/* 6. Desplegable (se muestra si P4 = "No") */
INSERT INTO dbo.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, IsRequired, SettingsJson)
VALUES (@V, 8, 'q6', 6, N'¿Cuál es el principal motivo por el que no la utilizas?', 1, N'{"Validation":{}}');
SET @Q6 = SCOPE_IDENTITY();
INSERT INTO dbo.QuestionOption (QuestionId, Role, ClientKey, Text, SortOrder) VALUES
 (@Q6, 1, 'q6o1', N'No conozco la herramienta', 1), (@Q6, 1, 'q6o2', N'No tengo acceso', 2),
 (@Q6, 1, 'q6o3', N'Prefiero otros recursos', 3), (@Q6, 1, 'q6o4', N'No tengo tiempo', 4);

/* Reglas de P4: Sí -> mostrar P5 · No -> mostrar P6 · No aplica -> finalizar */
INSERT INTO dbo.ConditionalRule (QuestionId, Operator, CompareValue, Action, TargetQuestionId, SortOrder) VALUES
 (@Q4, 'equals', 'q4o1', 'show', @Q5, 1),
 (@Q4, 'equals', 'q4o2', 'show', @Q6, 2),
 (@Q4, 'equals', 'q4o3', 'end', NULL, 3);

/* 7. Matriz (una respuesta por fila) */
INSERT INTO dbo.Question (SurveyVersionId, QuestionTypeId, ClientKey, SortOrder, Text, HelpText, IsRequired, SettingsJson)
VALUES (@V, 9, 'q7', 7, N'Evalúa los siguientes aspectos', N'Selecciona una opción por fila.', 1,
    N'{"Validation":{},"MatrixSelection":"single"}');
SET @Q = SCOPE_IDENTITY();
INSERT INTO dbo.QuestionOption (QuestionId, Role, ClientKey, Text, SortOrder) VALUES
 (@Q, 2, 'q7r1', N'Facilidad de uso', 1), (@Q, 2, 'q7r2', N'Rapidez', 2), (@Q, 2, 'q7r3', N'Soporte', 3), (@Q, 2, 'q7r4', N'Claridad del material', 4),
 (@Q, 3, 'q7c1', N'Muy insatisfecho', 1), (@Q, 3, 'q7c2', N'Insatisfecho', 2), (@Q, 3, 'q7c3', N'Neutral', 3),
 (@Q, 3, 'q7c4', N'Satisfecho', 4), (@Q, 3, 'q7c5', N'Muy satisfecho', 5);

/* Segmentación: ejecutivos y gerentes de Banca Personal y Premier */
INSERT INTO dbo.SurveyAudienceRule (SurveyVersionId, Dimension, Value) VALUES
 (@V, 'Role', 'ejecutivos'), (@V, 'Role', 'gerentes'), (@V, 'Segment', 'bp'), (@V, 'Segment', 'premier');

COMMIT TRANSACTION;

PRINT 'Encuesta de ejemplo creada en borrador. SurveyId = ' + CAST(@SurveyId AS VARCHAR(10));
