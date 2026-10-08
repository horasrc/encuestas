# Módulo de Encuestas Dinámicas

ASP.NET MVC 5 (.NET Framework 4.5) · Razor · jQuery 3.7.1 · Bootstrap 5.2.3 · Bootstrap Icons

## Probar sin Visual Studio
Servir la carpeta `Encuestas.Web` con un servidor local y abrir `demo-index.html` (listado),
`demo.html?id=s1` (constructor) o `demo-responder.html?id=s1` (colaborador).
Estas páginas se **generan** a partir de las vistas Razor:

    powershell -ExecutionPolicy Bypass -File tools\build-demo.ps1

## Abrir en Visual Studio
Requisitos: Visual Studio 2019 o 2022 con la carga de trabajo **«Desarrollo de ASP.NET y web»**.

`Encuestas.sln` → clic derecho en la solución → **Restaurar paquetes NuGet** → F5 → `/Encuestas`.

El proyecto se mantiene en **.NET Framework 4.5**. Visual Studio 2022 ya no instala el targeting pack de 4.5,
así que el proyecto usa el paquete NuGet `Microsoft.NETFramework.ReferenceAssemblies.net45` (solo para compilar).
**Si al abrir aparece «Plataforma de destino no compatible» (ofrece actualizar a 4.8):** elige
«No cargar este proyecto», cierra Visual Studio y, en PowerShell **como administrador**, ejecuta una sola vez:

    powershell -ExecutionPolicy Bypass -File tools\install-net45-refs.ps1

Copia los ensamblados de referencia 4.5 del paquete NuGet a `Reference Assemblies` (solo agrega archivos).
Después el proyecto carga normalmente. No aceptes el cambio a 4.8: el servidor necesita 4.5.

Si compilas antes de restaurar NuGet verás el error «Faltan paquetes NuGet»: restaura y compila de nuevo.
`Web.config` → `Surveys:UseMockData`: `true` usa datos mock (localStorage); `false` usa `SurveyApiController`.

## Publicar en el servidor
    powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -SqlServer "SERVIDOR\INSTANCIA" -AdminRoles "DOMINIO\Grupo-Admin"

Genera `publish\Encuestas.Web` (Release, vistas precompiladas, sin datos de prueba). Instalación en IIS: **[DEPLOY.md](DEPLOY.md)**.

## Pantallas
| URL | Quién | Qué |
|---|---|---|
| `/Encuestas` | Administrador | Listado (filtro por estado, búsqueda), «Nueva encuesta», duplicar y eliminar (solo sin respuestas) |
| `/Encuestas/Constructor/{id}` | Administrador | Constructor, configuración, vista previa y resultados (`#resultados` abre esa pestaña) |
| `/Encuestas/Responder/{id}` | Colaborador | Antes de mostrarla verifica si puede responder (vigencia, sesión, ya respondió, audiencia); luego responde la versión publicada |

## Estructura
| Capa | Archivos |
|---|---|
| Vistas | `Index.cshtml` (listado); `Constructor.cshtml` + parciales `_BuilderHeader`, `_TabConstructor`, `_TabSettings`, `_TabPreview`, `_TabResults`, `_Modals`; `Responder.cshtml` |
| JS común | `survey-utils` · `survey-types` (catálogo de tipos) · `survey-logic` (motor condicional) · `survey-renderer` (dibuja preguntas) |
| JS datos | `survey-api` (única capa de acceso: mock o servidor) · `mock-data` · `survey-store` (estado + eventos) |
| JS UI | `survey-list` · `survey-builder` · `survey-editor` · `survey-settings` · `survey-preview` · `survey-results` · `survey-app` (arranque) · `survey-respond` |
| Back-end | `Controllers/SurveyApiController` · `Services/SurveyService` (versionado, publicación, envío) · `Services/SurveyLogic` (lógica condicional y validación en servidor) · `Services/ISurveyRepository` |
| Seguridad | `Infrastructure/SurveyAdminAuthorizeAttribute` · `Infrastructure/ValidateJsonAntiForgeryTokenAttribute` |
| BD | `Database/schema.sql` · `seed.sql` · `upgrade-001-audience-mode.sql` · `org-schema.sql` / `org-seed.sql` (catálogos de la organización) |
| Pruebas | `tests/ServiceTests.cs` · `tests/run-tests.ps1` (reglas de negocio, sin VS ni BD) |

## Pruebas
    powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1

Compila con el `csc` de .NET Framework y ejecuta 30 pruebas de `SurveyService` / `SurveyLogic` sobre el repositorio en memoria.

Integración (sitio corriendo en `http://localhost:51842` contra LocalDB; **modifica datos**):

    powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Recreate
    powershell -ExecutionPolicy Bypass -File tests\e2e-tests.ps1

32 pruebas: autenticación de Windows, token antifalsificación, guardado/publicación en SQL, lógica condicional
en servidor, una respuesta por usuario, resultados, versionado, crear/duplicar/eliminar.

## Conectar la base de datos (SqlSurveyRepository)
**Desarrollo (LocalDB, ya configurado en `Web.config`):**

    powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1            # crea PortalEncuestas con datos de ejemplo
    powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Recreate  # la borra y la vuelve a crear

**Otro servidor:** `tools\setup-db.ps1 -Server "SERVIDOR\INSTANCIA" -NoSamples`, o ejecutar los scripts a mano:
1. `Database/schema.sql` (y `seed.sql` si quieres la encuesta de ejemplo).
   Si la base se creó con una versión anterior de `schema.sql`, ejecutar `upgrade-001-audience-mode.sql`.
2. Catálogos de la organización: `Database/org-schema.sql`. En desarrollo, `org-seed.sql` carga datos
   de ejemplo e incluye **tu propio usuario** como gerente (para probar la audiencia). En producción, RH/ETL llena
   las tablas `Org*` (o se reemplazan por vistas con los mismos nombres y columnas).
3. `Web.config`:
   - `connectionStrings/SurveysDb` → tu servidor.
   - `Surveys:UseMockData = false`, `Surveys:Repository = Sql`, `Surveys:CatalogProvider = Sql`.
   - Para autenticación integrada, la identidad del AppPool necesita permisos de lectura/escritura en la BD.
4. Opcional: quitar `mock-data.js` de `_SurveyScripts.cshtml`.

Cómo guarda `Services/SqlSurveyRepository.cs`:
- Encuesta/versión ↔ `SurveyId`/`SurveyVersionId` (como texto). Preguntas, opciones y dependientes ↔ `ClientKey`.
- Versión editable (borrador o publicada sin respuestas): su contenido se reemplaza completo en una transacción.
- Versión con respuestas: solo cambia su estado (cerrar/archivar); el trigger `TR_Question_Immutable` lo refuerza.
- Respuestas: una fila por valor en `QuestionResponse`; `ResponseCount` se incrementa en la misma transacción.
- Segmentación: `SurveyVersion.AudienceMode` (0 todos, 1 segmentada) + filtros en `SurveyAudienceRule`.
- Catálogos de segmentación: `SqlOrgCatalogProvider` (tablas `Org*`, en caché `Surveys:CatalogCacheMinutes`).
  La plantilla por sucursal se calcula contando colaboradores activos, así la audiencia estimada es real.

## Seguridad y validación
- Todo el sitio exige usuario de Windows (`<deny users="?" />` en `Web.config`). Con Windows Authentication la
  identidad es por conexión: si se permitiera el acceso anónimo, algunos POST llegarían sin usuario y el token
  antifalsificación fallaría. En IIS: habilitar **Autenticación de Windows** y deshabilitar la anónima.
- `Surveys:AdminRoles`: grupos de Windows que administran encuestas (vacío = cualquier usuario autenticado; solo para desarrollo).
- Todos los POST de la API exigen el token antifalsificación (encabezado `RequestVerificationToken`, lo envía `survey-api.js`).
- Quien responde solo recibe la versión publicada (`/SurveyApi/Published/{id}`), sin borradores ni segmentación.
- Al enviar, el servidor recalcula la lógica condicional (`SurveyLogic.cs`, puerto de `survey-logic.js`):
  exige solo las obligatorias visibles, aplica límites y descarta respuestas de preguntas ocultas u opciones inexistentes.
  **Si cambias la lógica en `survey-logic.js` o las validaciones de `survey-renderer.js`, replica el cambio en `SurveyLogic.cs`.**
- `Surveys:EnforceAudience = true`: rechaza a quien no pertenece a la segmentación (compara `User.Identity.Name`
  con `OrgUser.Login`). Requiere `Surveys:CatalogProvider = Sql` con usuarios reales.

## Agregar un tipo de pregunta
`survey-types.js` → `survey-renderer.js` → `survey-editor.js` → `survey-results.js` → `SurveyLogic.cs` (validación) → fila en `QuestionType`.
