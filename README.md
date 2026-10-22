# Módulo de Encuestas Dinámicas

ASP.NET MVC 5 (.NET Framework 4.5) · Razor · jQuery 3.7.1 · Bootstrap 5.2.3 · Bootstrap Icons

El módulo es un **Área de MVC autocontenida** (`Encuestas.Web\Areas\Encuestas`) con sus tablas en el
esquema SQL **`enc`**. Para incrustarlo en el portal: **[INTEGRACION.md](INTEGRACION.md)**.
El resto de `Encuestas.Web` (Global.asax, RouteConfig, HomeController, Web.config) es solo un
"portal de ejemplo" para desarrollar y probar el módulo.

## Probar sin Visual Studio
Servir la carpeta `Encuestas.Web` con un servidor local y abrir `demo-index.html` (listado),
`demo.html?id=s1` (constructor) o `demo-responder.html?id=s1` (colaborador).
Estas páginas se **generan** a partir de las vistas Razor del Área:

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
`Web.config` → `Surveys:UseMockData`: `true` usa datos mock (localStorage); `false` usa la API del módulo.

## Publicar en el servidor
    powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -SqlServer "SERVIDOR\INSTANCIA"

Genera `publish\Encuestas.Web` (Release, vistas precompiladas, sin datos de prueba). Instalación en IIS: **[DEPLOY.md](DEPLOY.md)**.
Para instalarlo **dentro del portal** (lo habitual), sigue **[INTEGRACION.md](INTEGRACION.md)**.

## Pantallas
| URL | Quién | Qué |
|---|---|---|
| `/Encuestas` | Administrador (lo restringe el portal) | Listado (filtro por estado, búsqueda), «Nueva encuesta», duplicar y eliminar (solo sin respuestas) |
| `/Encuestas/Constructor/{id}` | Administrador (lo restringe el portal) | Constructor, configuración, vista previa y resultados (`#resultados` abre esa pestaña). El panel **Versiones** muestra la **actividad** (bitácora) |
| `/Encuestas/Responder/{id}` | Colaborador | Antes de mostrarla verifica si puede responder (vigencia, sesión, ya respondió, audiencia); luego responde la versión publicada |
| `/Encuestas/Api/...` | (JavaScript) | API JSON del módulo |

## Estructura
```
Encuestas.Web\Areas\Encuestas\
  EncuestasAreaRegistration.cs   rutas (/Encuestas, /Encuestas/Api)
  Controllers\    EncuestasController (páginas) · SurveyApiController (API JSON)
  Services\       SurveyService (versionado, publicación, envío, bitácora) · SurveyLogic (lógica condicional
                  y validación en servidor) · SqlSurveyRepository · SqlOrgCatalogProvider · ISurveyRepository
  Models\         Dto · Entities · ViewModels
  Infrastructure\ SurveyUser · ValidateJsonAntiForgeryToken · JsonNetResult · Url.Versioned
  Views\          Encuestas\ (Index, Constructor + parciales, Responder) · Shared\_LayoutEncuestas · Web.config
  Scripts\        survey-utils · survey-types · survey-logic · survey-renderer · survey-api · survey-store ·
                  survey-list · survey-builder · survey-editor · survey-settings · survey-preview ·
                  survey-results · survey-app · survey-respond · mock-data
  Content\        survey.css · lib\ (Bootstrap 5.2.3, jQuery 3.7.1, Bootstrap Icons 1.11.3, locales)
Database\         schema.sql · seed.sql · org-schema.sql · org-seed.sql · upgrade-NNN-*.sql   (esquema enc)
tools\            setup-db.ps1 · publish.ps1 · build-demo.ps1 · install-net45-refs.ps1
tests\            ServiceTests.cs + run-tests.ps1 (unitarias) · e2e-tests.ps1 (integración)
```
Espacio de nombres del módulo: `ModuloEncuestas` (no depende del nombre del proyecto que lo contiene).

Además, `Areas\Citas\` es un ejemplo independiente de formulario de citas que, al aceptar, agrega la cita al
calendario de Outlook (archivo `.ics`). Se prueba en `/Citas`; cómo llevarlo al portal: **[CITAS-OUTLOOK.md](CITAS-OUTLOOK.md)**.
`Areas\Citas\Scripts\cita-outlook.js` hace lo mismo solo en el navegador, para formularios que guardan con
JavaScript (como "Registro de citas / visitas"): `CitaOutlook.descargar(formData)`.

## Pruebas
    powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1

Compila con el `csc` de .NET Framework y ejecuta 65 pruebas: 55 de `SurveyService` / `SurveyLogic` sobre el repositorio en memoria
(incluye desplegables en cascada e identificador) y 10 del archivo `.ics` de Citas (`CalendarioIcs`).

Integración (sitio corriendo contra LocalDB; **modifica datos**, usa una base de pruebas):

    powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Database PortalEncuestasPruebas -Recreate
    powershell -ExecutionPolicy Bypass -File tests\e2e-tests.ps1 -Base http://localhost:51842

32 pruebas: autenticación de Windows, token antifalsificación, guardado/publicación en SQL, lógica condicional
en servidor, una respuesta por usuario, resultados, versionado, crear/duplicar/eliminar.

## Base de datos (esquema `enc`)
**Desarrollo (LocalDB, ya configurado en `Web.config`):**

    powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1            # crea PortalEncuestas con datos de ejemplo
    powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Recreate  # la borra y la vuelve a crear (solo LocalDB)

**Base del portal:** ver [INTEGRACION.md](INTEGRACION.md) (paso 4). El script crea solo el esquema `enc` y nunca
borra ni crea la base del portal. Las actualizaciones (`upgrade-*.sql`) se aplican con el mismo comando.

Cómo guarda `Services/SqlSurveyRepository.cs`:
- Encuesta/versión ↔ `SurveyId`/`SurveyVersionId` (como texto). Preguntas, opciones y dependientes ↔ `ClientKey`.
- Versión editable (borrador o publicada sin respuestas): su contenido se reemplaza completo en una transacción.
- Versión con respuestas: solo cambia su estado (cerrar/archivar); el trigger `enc.TR_Question_Immutable` lo refuerza.
- Respuestas: una fila por valor en `enc.QuestionResponse`; `ResponseCount` se incrementa en la misma transacción.
- Segmentación: `SurveyVersion.AudienceMode` (0 todos, 1 segmentada) + filtros en `SurveyAudienceRule`.
- Catálogos de segmentación: `SqlOrgCatalogProvider` (`enc.Org*`, tablas o vistas sobre el portal; en caché
  `Surveys:CatalogCacheMinutes`). La plantilla por sucursal se calcula contando colaboradores activos.
- Cadena de conexión: la indicada en `Surveys:ConnectionStringName` (por defecto `SurveysDb`).
- Esquema: `Surveys:Schema` (por defecto `enc`). Las consultas usan `enc.` y `SurveySchema` lo cambia; los scripts
  para otro esquema se generan con `tools\esquema-sql.ps1 -Esquema IC` (ya incluidos en `Database\esquema-IC`).

## Seguridad y validación
- **El módulo no controla el acceso**: quién entra, quién administra y quién responde lo decide la seguridad del
  portal (ver [INTEGRACION.md](INTEGRACION.md), paso 3). El módulo solo necesita saber **quién es** el usuario
  (`Infrastructure/SurveyUser.cs`: por defecto `User.Identity.Name`; el portal puede asignar `SurveyUser.Resolver`).
- El sitio de prueba de este repositorio simula un portal con usuario de Windows obligatorio (`<deny users="?" />`).
  Con Windows Authentication la identidad es por conexión: si se permitiera el acceso anónimo, algunos POST llegarían
  sin usuario y el token antifalsificación fallaría. En IIS: habilitar **Autenticación de Windows** y deshabilitar la anónima.
- Todos los POST de la API exigen el token antifalsificación (encabezado `RequestVerificationToken`, lo envía `survey-api.js`).
- Quien responde solo recibe la versión publicada (`/Encuestas/Api/Published/{id}`), sin borradores ni segmentación.
- Al enviar, el servidor recalcula la lógica condicional (`SurveyLogic.cs`, puerto de `survey-logic.js`):
  exige solo las obligatorias visibles, aplica límites y descarta respuestas de preguntas ocultas u opciones inexistentes.
  **Si cambias la lógica en `survey-logic.js` o las validaciones de `survey-renderer.js`, replica el cambio en `SurveyLogic.cs`.**
- `Surveys:EnforceAudience = true`: rechaza a quien no pertenece a la segmentación (compara el usuario de `SurveyUser`
  con `OrgUser.Login`). Requiere `Surveys:CatalogProvider = Sql` con usuarios reales.

## Desplegables en cascada e identificador
- **Desplegable en cascada:** en un *Desplegable*, «¿Las opciones dependen de otra pregunta?» elige una pregunta
  anterior de opciones; a cada opción se le indica con qué respuesta aparece («Siempre» = con cualquiera). Al responder,
  la lista se llena según lo elegido. Si para lo elegido no hay opciones, la pregunta no se exige. El servidor descarta
  opciones que no correspondan (`SurveyLogic.CascadeOptions`). Se guarda en `Question.SettingsJson`: **no cambia tablas**.
- **Identificador** (ej. número de cliente): una pregunta de *Texto libre* marcada «Es identificador». Quien responde
  escribe el valor y presiona **Buscar**: `POST /Encuestas/Api/LastResponse` devuelve la última captura de la encuesta con
  ese valor (cualquier versión; entre versiones se emparejan preguntas y opciones por su texto) y se precarga para
  actualizarla. Cada envío queda como captura nueva (historial). Para que una persona capture varios clientes, la
  encuesta debe tener **Múltiples respuestas**. La búsqueda exige lo mismo que responder (vigencia, sesión, audiencia).

## Agregar un tipo de pregunta
`survey-types.js` → `survey-renderer.js` → `survey-editor.js` → `survey-results.js` → `SurveyLogic.cs` (validación) → fila en `enc.QuestionType`.
