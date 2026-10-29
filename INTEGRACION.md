# Integrar el módulo de encuestas en el portal

El módulo es un **Área de ASP.NET MVC** autocontenida: `Encuestas.Web\Areas\Encuestas\`.
Sus tablas viven en el esquema **`enc`** dentro de la base del portal.

| Se copia | Se agrega al portal | No se toca del portal |
|---|---|---|
| La carpeta `Areas\Encuestas` (código, vistas, JS, CSS, librerías) | Unas líneas de `Web.config`, un paquete NuGet, las tablas `enc.*` | Sus controladores, vistas, layout, rutas ni tablas `dbo.*` |

URLs del módulo: `/Encuestas` (listado), `/Encuestas/Constructor/{id}`, `/Encuestas/Pendientes`, `/Encuestas/Responder/{id}`
y la API `/Encuestas/Api/...`. Dentro de Radar se muestra con una vista parcial (paso 5). Quién administra, en qué
segmento y su tramo de control los decide el portal (paso 3): el módulo no tiene administración de usuarios.

---

## Requisitos del portal
- ASP.NET **MVC 5** sobre .NET Framework **4.5** o posterior.
- **Autenticación de Windows** (el módulo identifica a las personas con `User.Identity.Name`).
- **SQL Server 2012** o posterior.
- El `Global.asax.cs` del portal debe llamar a `AreaRegistration.RegisterAllAreas();` (las plantillas de MVC lo traen).

## 1. Copiar el Área
1. Copia `Encuestas.Web\Areas\Encuestas` a la raíz del proyecto del portal, de modo que quede `Portal\Areas\Encuestas\`.
2. En Visual Studio: **Explorador de soluciones → Mostrar todos los archivos**, clic derecho sobre `Areas\Encuestas` → **Incluir en el proyecto**.
3. Revisa que los archivos de `Content\lib\fonts\*.woff*` y `Areas\Encuestas\Web.config` queden con **Acción de compilación = Contenido**.

Contenido de la carpeta:
```
Areas\Encuestas\
  EncuestasAreaRegistration.cs   rutas del módulo
  Controllers\                   EncuestasController (páginas) · SurveyApiController (API JSON)
  Services\                      reglas de negocio, lógica condicional, repositorios SQL
  Models\                        DTOs, entidades, view models
  Infrastructure\                autorización, token antifalsificación, JSON, URLs versionadas
  Views\                         vistas Razor + layout propio + Web.config de Razor
  Scripts\  Content\             JS y CSS del módulo + Bootstrap 5.2.3, jQuery 3.7.1, Bootstrap Icons (locales)
  Web.config                     tipos MIME de las fuentes de iconos
```
El código usa el espacio de nombres `ModuloEncuestas`, que no choca con el del portal.

## 2. Paquete NuGet
El módulo usa **Newtonsoft.Json 13.0.1 o posterior** (las versiones anteriores tienen una vulnerabilidad conocida).
- Si el portal ya lo tiene en 13.x: nada que hacer.
- Si tiene una versión anterior: actualízalo (Herramientas → Administrador de paquetes NuGet) y revisa que el
  `bindingRedirect` de `Newtonsoft.Json` en el `Web.config` del portal quede en `13.0.0.0`.

Si el portal usa una versión de MVC distinta de **5.2.3**, ajusta los números de versión de
`System.Web.Mvc` y `System.Web.WebPages.Razor` en `Areas\Encuestas\Views\Web.config` para que coincidan con
los de `Views\Web.config` del portal (cópialos de ahí).

## 3. `Web.config` del portal
Dentro de `<appSettings>`:
```xml
<add key="Surveys:UseMockData" value="false" />
<add key="Surveys:Repository" value="Sql" />
<!-- Nombre de la cadena de conexión del PORTAL (las tablas enc.* viven en su base). -->
<add key="Surveys:ConnectionStringName" value="NombreDeLaCadenaDelPortal" />
<!-- Layout del portal (ver paso 6; no aplica dentro de la vista parcial de Radar). Vacío = layout propio. -->
<add key="Surveys:Layout" value="~/Views/Shared/_Layout.cshtml" />
```
Si el portal no tiene una cadena de conexión con seguridad integrada, agrega una propia y apunta
`Surveys:ConnectionStringName` a ella (por defecto busca `SurveysDb`).

### Seguridad y tramo de control: los decide el portal
El módulo **no tiene administración de usuarios** (ni puestos, ni sucursales, ni "quién puede responder").
Según quién inició sesión, el portal le dice al módulo tres cosas, una sola vez en `Application_Start` del
`Global.asax.cs` del portal:
```csharp
// using ModuloEncuestas.Infrastructure;
SurveyAccess.Resolver = http => new SurveyScope
{
    CanAdminister = EsAdministradorDeEncuestas(http),   // ← regla del portal: crear, editar, publicar, resultados
    Segments      = SegmentosDelUsuario(http),          // ← segmentos en que trabaja (null = todos)
    Team          = UsuariosDeSuTramo(http)             // ← su tramo de control: usuarios (null = ve todo)
};
```
| Lo que indica el portal | Lo que hace el módulo |
|---|---|
| `CanAdminister = false` | `/Encuestas` le muestra **Encuestas por responder**; el constructor y la API de administración (`List`, `Survey`, `History`, `Results`, `Create`, `Duplicate`, `Delete`, `SaveDraft`, `Publish`, `CreateVersion`, `ChangeStatus`) responden **403**. |
| Segmento (el `segmento` que recibe la vista parcial) | Cada encuesta nueva **queda en ese segmento** y solo aparece ahí (listado, pendientes, responder). Si el segmento no está en `Segments`, responde **403**. Las encuestas creadas antes (sin segmento) aparecen en todos. |
| `Team` | En **Resultados** solo cuentan las respuestas de esas personas (también en encuestas anónimas); la tasa de respuesta se calcula sobre el tamaño del tramo. |

- `Segments` y `Team` usan los **mismos valores** que devuelve `SurveyUser` (normalmente `DOMINIO\usuario`).
- Si el portal **no** asigna `SurveyAccess.Resolver`, el módulo no restringe nada (todos administran y ven todo).
- `SurveyAccess.Resolver` se llama una vez por petición; si consulta la base, conviene que el portal guarde el
  resultado en su sesión o en caché.
- El proyecto de prueba trae un ejemplo funcionando con usuarios simulados: `Encuestas.Web\Global.asax.cs`
  (`SimularTramoDeControl`) y la página `/RadarPrueba`.

**Quién es el usuario.** Por defecto el módulo toma `User.Identity.Name` (autenticación de Windows o Forms). Lo
usa para la bitácora, "ya respondiste" y el tramo de control. Si el portal guarda al usuario de otra forma (por
ejemplo, en la sesión), indícalo también en `Application_Start`:
```csharp
ModuloEncuestas.Infrastructure.SurveyUser.Resolver =
    http => http.Session == null ? null : (string)http.Session["Usuario"];   // ← como lo guarde el portal
```
Debe devolver el mismo tipo de valor que el portal pone en `Team`, o `null` si no hay usuario.

Con autenticación de Windows **la autenticación anónima debe estar deshabilitada en IIS** para el sitio (o al
menos para `/Encuestas`); si no, al guardar o enviar puede aparecer "La página expiró por seguridad". El token
antifalsificación de los POST se conserva: protege contra envíos desde otros sitios y no restringe a nadie.

Recomendado (lo usa el proyecto de ejemplo en `Global.asax.cs` y `App_Start\RouteConfig.cs`):
- En la ruta `Default` del portal, `route.DataTokens["UseNamespaceFallback"] = false;` con el espacio de
  nombres de los controladores del portal. Así los controladores del módulo solo responden en `/Encuestas/...`.

## 4. Base de datos (dentro de la base del portal)

### Opción A: con SQL Server Management Studio (SSMS)
1. Conéctate al servidor y, en el selector de bases de la barra de herramientas, **elige la base del portal**
   (o empieza cada script con `USE NombreDeLaBaseDelPortal;`).
2. **Archivo → Abrir → Archivo** → `Database\schema.sql` → **Ejecutar** (F5).
   Termina con el mensaje `Esquema enc: 12 tablas del módulo.`
3. Da permisos a la cuenta del sitio (ver abajo).

**No ejecutes** `seed.sql` en el portal: son datos de ejemplo para desarrollo. Ya no hay tablas `Org*`
(empleados, sucursales…): quién responde lo decide el portal. Si ya las creaste, el módulo no las usa
(ver `Database\obsoleto\LEEME.txt`).
Los scripts solo crean objetos en el esquema `enc`: no modifican ni leen ninguna tabla del portal. Si se
ejecutan dos veces, se detienen con un mensaje ("ya existen…") sin hacer cambios.

### Si no te permiten crear el esquema `enc`: usar uno que ya existe (ej. `IC`)
Crear un esquema exige ser `db_owner` (error *"Cannot find the user 'dbo'"* al ejecutar `schema.sql`). Si el
portal ya tiene un esquema donde sí puedes crear tablas, las del módulo pueden ir ahí:
1. Comprueba el permiso en ese esquema (ambos deben dar `1`):
   ```sql
   SELECT HAS_PERMS_BY_NAME('IC', 'SCHEMA', 'ALTER') AS alter_esquema,
          HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') AS crear_tablas;
   ```
2. Usa los scripts de **`Database\esquema-IC\`** (son los mismos con `IC` en lugar de `enc`; para otro esquema se
   generan con `tools\esquema-sql.ps1 -Esquema NOMBRE`). Ejecuta `schema.sql` como arriba; termina con
   `Esquema IC: 12 tablas del módulo.` Antes de crear nada revisa que ninguna tabla del esquema se llame igual
   que las del módulo (`Survey`, `Question`…); si alguna choca, se detiene sin cambios.
   - Si falla con *"The REFERENCES permission was denied on the object 'Survey'"* (puedes crear tablas pero no
     llaves foráneas): borra la tabla que quedó a medias (`DROP TABLE IC.Survey;`, solo si es la única del
     módulo que se creó) y usa los scripts de **`Database\esquema-IC-sin-FK\`** (`tools\esquema-sql.ps1 -Esquema IC
     -SinLlavesForaneas`). Son iguales pero sin `FOREIGN KEY`: el módulo funciona igual, porque guarda y borra en
     orden dentro de transacciones; solo se pierde que la base impida datos inconsistentes si alguien edita las
     tablas a mano. La alternativa es pedir `GRANT REFERENCES ON SCHEMA::IC TO [tu usuario]` y usar los normales.
3. En el `Web.config`: `<add key="Surveys:Schema" value="IC" />`.
4. Los permisos de la cuenta del sitio van sobre ese esquema: `GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::IC TO [...]`
   (si la cuenta ya puede leer y escribir en `IC`, no hace falta).

### Opción B: con el script de PowerShell
Desde el equipo de desarrollo, con permisos para crear tablas en la base del portal:
```
powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Server "SQLPROD01\PORTAL" -Database BaseDelPortal -NoSamples
```
- Crea el esquema `enc` y sus tablas. **No toca ninguna tabla `dbo.*` del portal.**
- Si la base no existe, el script se detiene (no crea bases fuera de LocalDB).
- `-Recreate` está bloqueado fuera de LocalDB: nunca puede borrar la base del portal.
- Más adelante, el mismo comando aplica las actualizaciones (`Database\upgrade-*.sql`).

Permisos para la cuenta con la que se conecta el sitio (solo sobre el esquema del módulo):
```sql
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::enc TO [MIDOMINIO\cuenta-del-sitio];
```

## 5. Dentro de Radar: vista parcial (recomendado)
Radar carga sus módulos como vistas parciales desde `RadarController` (por ejemplo `MiTuberia(string segmento)`).
Encuestas se agrega igual. En `RadarController` del portal:
```csharp
public PartialViewResult Encuestas(string segmento)
{
    return PartialView(ModuloEncuestas.Controllers.EncuestasController.VistaParcial,
        new ModuloEncuestas.Models.ViewModels.SurveyEmbedViewModel { Segmento = segmento });
}
```
y en el menú de Radar se llama como a los demás módulos, con el segmento.

- La vista parcial (`Areas\Encuestas\Views\Encuestas\_Encuestas.cshtml`) es un **marco (`iframe`)** del mismo
  sitio con las páginas del módulo. Por eso **funciona sin importar cómo Radar inserte la vista** (`.html()`,
  `.load()` o `innerHTML`) ni si ejecuta sus `<script>` (en Citas un `<script>` aparte no corría), y los estilos
  del portal (por ejemplo, los `.btn` blancos) no le afectan.
- Dentro del marco se navega entre listado, constructor, resultados y responder sin salir de Radar; el
  segmento viaja en cada página y en cada llamada a la API.
- El marco se ajusta solo al alto disponible de la pantalla.
- `Vista`: vacío = listado si administra o **Encuestas por responder** si no; `"responder"` = siempre
  "por responder" (útil para un botón aparte). Con `EncuestaId` abre directamente esa encuesta para responder.
- Requisito: el portal no debe prohibir marcos de su mismo sitio. Si su `Web.config` envía
  `X-Frame-Options`, debe ser `SAMEORIGIN` (no `DENY`).

Probado con `/RadarPrueba` del proyecto de ejemplo, que imita Radar (estilos agresivos, inserción con
`.html()` y con `innerHTML`) y simula usuarios con distinto tramo.

## 6. Layout: usar el del portal (Bootstrap 5)
Si en lugar de la vista parcial se abren las páginas `/Encuestas` directamente:
Como el portal usa **Bootstrap 5**, el módulo puede mostrarse **dentro del layout del portal** (su menú,
encabezado y pie). Solo agrega en `<appSettings>`:
```xml
<add key="Surveys:Layout" value="~/Views/Shared/_Layout.cshtml" />   <!-- ruta del layout del portal -->
```
Lo único que el layout del portal debe tener es:
```cshtml
@RenderSection("scripts", required: false)
```
antes de `</body>` (las plantillas de MVC lo traen). Si falta, las páginas del módulo darán un error que
indica que la sección `scripts` se definió pero no se representó en la página de diseño.

El módulo se encarga del resto:
- Agrega su CSS (`survey.css`) y **Bootstrap Icons** al inicio de su contenido.
- Si el portal **no** carga **jQuery** o el **JavaScript de Bootstrap** antes de esa sección, carga sus propias
  copias; si ya los carga, usa los del portal y no los duplica.
- Funciona con cualquier Bootstrap **5.x** (los estilos del módulo no dependen de las variables CSS de 5.2+).

Esto se probó con un layout de ejemplo que imita el peor caso (Bootstrap 5 sin jQuery ni iconos):
`Encuestas.Web\Views\Shared\_LayoutPortalPrueba.cshtml` — listado, constructor (pestañas, modales, panel de
versiones) y página de respuesta, en escritorio, tablet y móvil, sin desbordes ni errores.

Con `Surveys:Layout` vacío, el módulo usa su propio layout (`_LayoutEncuestas.cshtml`, con un menú lateral
de ejemplo). La página para responder (`Responder.cshtml`) nunca usa layout: siempre se ve limpia, solo con
la encuesta.

Enlace para el menú del portal:
```cshtml
<a href="@Url.Action("Index", "Encuestas", new { area = "Encuestas" })">Encuestas</a>
```

## 7. Verificación
1. Compila el portal y abre Encuestas desde Radar (segmento A) con un usuario que administra → listado.
2. Crea una encuesta, agrega una pregunta, **Publicar**.
3. Con un usuario del mismo segmento que **no** administra: ve la encuesta en **Encuestas por responder** y la
   responde. Con uno de otro segmento: no la ve.
4. Como el jefe: en **Resultados** solo aparecen las respuestas de su tramo.
5. En SQL: `SELECT * FROM enc.SurveyAuditLog` muestra quién hizo cada acción (si sale `sistema`, el módulo no
   está recibiendo el usuario: revisa `SurveyUser.Resolver`).

## Quitar el módulo
1. Borra la carpeta `Areas\Encuestas` del proyecto y las líneas `Surveys:*` del `Web.config`.
2. En SQL (pierde todas las encuestas y respuestas): elimina las tablas del esquema `enc` y luego `DROP SCHEMA enc`.
