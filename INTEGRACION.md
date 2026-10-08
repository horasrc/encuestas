# Integrar el módulo de encuestas en el portal

El módulo es un **Área de ASP.NET MVC** autocontenida: `Encuestas.Web\Areas\Encuestas\`.
Sus tablas viven en el esquema **`enc`** dentro de la base del portal.

| Se copia | Se agrega al portal | No se toca del portal |
|---|---|---|
| La carpeta `Areas\Encuestas` (código, vistas, JS, CSS, librerías) | Unas líneas de `Web.config`, un paquete NuGet, las tablas `enc.*` | Sus controladores, vistas, layout, rutas ni tablas `dbo.*` |

URLs del módulo: `/Encuestas` (listado), `/Encuestas/Constructor/{id}`, `/Encuestas/Responder/{id}` y la API `/Encuestas/Api/...`.

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
<add key="Surveys:CatalogProvider" value="Sql" />
<add key="Surveys:CatalogCacheMinutes" value="10" />
<!-- Grupo(s) de Windows que administran encuestas. Nunca vacío en producción. -->
<add key="Surveys:AdminRoles" value="MIDOMINIO\Encuestas-Admin" />
<add key="Surveys:EnforceAudience" value="false" />
<!-- Nombre de la cadena de conexión del PORTAL (las tablas enc.* viven en su base). -->
<add key="Surveys:ConnectionStringName" value="NombreDeLaCadenaDelPortal" />
<!-- Layout del portal (ver paso 5). Vacío = layout propio del módulo. -->
<add key="Surveys:Layout" value="~/Views/Shared/_Layout.cshtml" />
```
Si el portal no tiene una cadena de conexión con seguridad integrada, agrega una propia y apunta
`Surveys:ConnectionStringName` a ella (por defecto busca `SurveysDb`).

Exigir usuario de Windows en el módulo (si el portal no lo hace ya para todo el sitio):
```xml
<location path="Encuestas">
  <system.web>
    <authorization>
      <deny users="?" />
    </authorization>
  </system.web>
</location>
```
Con autenticación de Windows **la autenticación anónima debe estar deshabilitada en IIS** para el sitio (o al
menos para `/Encuestas`); si no, al guardar o enviar puede aparecer "La página expiró por seguridad".

Recomendado (lo usa el proyecto de ejemplo en `Global.asax.cs` y `App_Start\RouteConfig.cs`):
- En la ruta `Default` del portal, `route.DataTokens["UseNamespaceFallback"] = false;` con el espacio de
  nombres de los controladores del portal. Así los controladores del módulo solo responden en `/Encuestas/...`.

## 4. Base de datos (dentro de la base del portal)

### Opción A: con SQL Server Management Studio (SSMS)
1. Conéctate al servidor y, en el selector de bases de la barra de herramientas, **elige la base del portal**
   (o empieza cada script con `USE NombreDeLaBaseDelPortal;`).
2. **Archivo → Abrir → Archivo** → `Database\schema.sql` → **Ejecutar** (F5).
   Termina con el mensaje `Esquema enc: 12 tablas.`
3. Catálogos de la organización:
   - Si el portal **ya tiene** empleados/sucursales: crea las vistas `enc.Org*` (ver más abajo) y **no**
     ejecutes `org-schema.sql`.
   - Si **no** los tiene: abre y ejecuta `Database\org-schema.sql` (crea 5 tablas `enc.Org*` vacías que
     llenará RH).
4. Da permisos a la cuenta del sitio (ver abajo).

**No ejecutes** `seed.sql` ni `org-seed.sql` en el portal: son datos de ejemplo para desarrollo.
Los scripts solo crean objetos en el esquema `enc`: no modifican ni leen ninguna tabla del portal. Si se
ejecutan dos veces, se detienen con un mensaje ("ya existen…") sin hacer cambios.

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

### Catálogos de la organización: tablas propias o vistas sobre el portal
El módulo lee `enc.OrgRole`, `enc.OrgSegment`, `enc.OrgRegion`, `enc.OrgBranch` y `enc.OrgUser`.
Si el portal ya tiene empleados y sucursales, **no copies datos**: ejecuta `setup-db.ps1` con `-NoOrgTables`
y crea vistas con esos nombres y columnas sobre las tablas del portal. Ejemplo (ajusta nombres):
```sql
CREATE VIEW enc.OrgUser AS
SELECT CAST(e.IdEmpleado AS VARCHAR(40)) AS UserId,
       e.UsuarioRed                      AS Login,      -- DOMINIO\usuario
       e.NombreCompleto                  AS Name,
       e.Correo                          AS Email,
       CAST(e.IdPuesto AS VARCHAR(40))   AS RoleId,
       CAST(e.IdSucursal AS VARCHAR(40)) AS BranchId,
       CAST(CASE WHEN e.Activo = 1 THEN 1 ELSE 0 END AS BIT) AS IsActive
FROM dbo.Empleados e;
```
Columnas que espera cada vista: `OrgRole/OrgSegment/OrgRegion (Id, Name, SortOrder, IsActive)`,
`OrgBranch (BranchId, Name, RegionId, SegmentId, IsActive)` y la de arriba para `OrgUser`
(los nombres exactos están en `Database\org-schema.sql`).

**Plantilla lista:** `Database\org-vistas-plantilla.sql` crea las 5 vistas. Cambia lo marcado con `TODO`
y ejecútala en SSMS. Si antes ejecutaste `org-schema.sql`, la plantilla borra esas tablas **solo si están
vacías**; si tienen datos, se detiene sin cambiar nada. Así puedes empezar con las tablas vacías y pasar a
vistas después. Con el `GRANT ... ON SCHEMA::enc` de arriba basta: la cuenta del sitio lee empleados y
sucursales a través de las vistas, sin permiso directo sobre las tablas `dbo` del portal (las vistas y las
tablas deben tener el mismo dueño, normalmente `dbo`).

## 5. Layout: usar el del portal (Bootstrap 5)
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

## 6. Verificación
1. Compila el portal y abre `/Encuestas` con un usuario del grupo administrador → listado.
2. Crea una encuesta, agrega una pregunta, **Publicar**, abre `/Encuestas/Responder/{id}` y responde.
3. Con un usuario que **no** es administrador: `/Encuestas` → *403*, pero `/Encuestas/Responder/{id}` sí abre.
4. En SQL: `SELECT * FROM enc.SurveyAuditLog` muestra quién hizo cada acción.

## Quitar el módulo
1. Borra la carpeta `Areas\Encuestas` del proyecto y las líneas `Surveys:*` del `Web.config`.
2. En SQL (pierde todas las encuestas y respuestas): elimina las tablas del esquema `enc` y luego `DROP SCHEMA enc`.
