# Instalación en el servidor (IIS + SQL Server)

Guía para instalar el módulo de encuestas **como sitio independiente** en un servidor Windows con IIS.
Requiere permisos de administrador en el servidor y en SQL Server (normalmente lo hace el área de infraestructura).

> **¿Lo vas a incrustar en el portal existente?** Ese es el caso habitual y se describe en
> **[INTEGRACION.md](INTEGRACION.md)**: allí no se crea un sitio ni una base nueva; se copia el Área al proyecto
> del portal y las tablas se crean en el esquema `enc` de su base. Las secciones 3, 4 y 6 de esta guía
> (IIS, permisos SQL y recomendaciones) también aplican a ese caso.

## 0. Requisitos del servidor
- Windows Server 2012 R2 o posterior con **IIS 8+**.
- **.NET Framework 4.5 o posterior** (4.8 recomendado; la aplicación está compilada para 4.5).
- Características de IIS (Administrador del servidor → Agregar roles y características → Servidor web):
  - Desarrollo de aplicaciones → **ASP.NET 4.x** (o 4.8) y **Extensiones de .NET Extensibility 4.x**
  - Seguridad → **Autenticación de Windows**
- SQL Server 2012 o posterior (cualquier edición).

## 1. Base de datos
1. El DBA crea la base (por seguridad, el script nunca crea bases fuera de LocalDB):
   `CREATE DATABASE PortalEncuestas;`
2. Desde el equipo de desarrollo, con permisos para crear tablas en esa base:

       powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Server "SQLPROD01\PORTAL" -Database PortalEncuestas -NoSamples

Crea el esquema `enc` con las tablas de encuestas (`schema.sql`) y de catálogos (`org-schema.sql`),
**sin datos de ejemplo**. Alternativa: ejecutar esos dos scripts en SQL Server Management Studio.

Catálogos de la organización: las tablas `OrgRole`, `OrgSegment`, `OrgRegion`, `OrgBranch` y `OrgUser`
deben llenarse desde RH / Active Directory (o reemplazarse por **vistas** con los mismos nombres y
columnas sobre las tablas que ya tengan). `OrgUser.Login` debe ser `DOMINIO\usuario`.

## 2. Generar el paquete
En el equipo de desarrollo:

    powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -SqlServer "SQLPROD01\PORTAL" -AdminRoles "MIDOMINIO\Encuestas-Admin"

- `-AdminRoles`: grupo(s) de Windows que pueden crear, editar, publicar y ver resultados (separados por coma).
  Quien no esté en esos grupos solo puede **responder**.
- Resultado: `publish\Encuestas.Web` (~3 MB). El script verifica que no quede `debug`, LocalDB ni datos de prueba.

## 3. IIS
1. Copiar `publish\Encuestas.Web` al servidor, por ejemplo `D:\Sitios\Encuestas`.
2. **Grupo de aplicaciones** (Application Pool) nuevo: `Encuestas`
   - Versión de .NET CLR: **v4.0** · Modo de canalización: **Integrada**
   - Identidad: `ApplicationPoolIdentity` (o una cuenta de servicio del dominio, ver paso 4).
3. **Sitio o aplicación**: agregar una aplicación (ej. `/Encuestas` dentro del portal) o un sitio,
   apuntando a la carpeta y usando el grupo `Encuestas`.
4. **Autenticación** de la aplicación (IIS → Autenticación):
   - **Autenticación de Windows: Habilitada**
   - **Autenticación anónima: Deshabilitada** ← importante: con acceso anónimo, guardar o enviar
     puede fallar con "La página expiró" (el token antifalsificación está ligado al usuario).
5. Permisos de carpeta: la identidad del grupo (`IIS AppPool\Encuestas`) necesita **lectura** sobre la carpeta.

## 4. Permisos en SQL Server
La aplicación se conecta con **seguridad integrada**, es decir, con la identidad del grupo de aplicaciones:

- Si el grupo usa `ApplicationPoolIdentity` y SQL está **en el mismo servidor**: crear el login `IIS APPPOOL\Encuestas`.
- Si SQL está **en otro servidor**: usar el login de la **cuenta de equipo** (`MIDOMINIO\SERVIDORWEB$`) o,
  mejor, configurar el grupo con una **cuenta de servicio del dominio** y crear el login para esa cuenta.

```sql
USE PortalEncuestas;   -- o la base del portal, si el módulo va incrustado
CREATE USER [MIDOMINIO\svc-encuestas] FOR LOGIN [MIDOMINIO\svc-encuestas];   -- si aún no existe
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::enc TO [MIDOMINIO\svc-encuestas];
```
La aplicación solo lee y escribe datos del esquema `enc`; no necesita permisos para crear tablas ni sobre
el resto de la base.

## 5. Verificación
1. Abrir `https://servidor/Encuestas` con un usuario del grupo administrador → debe verse el listado.
2. Crear una encuesta de prueba, agregar una pregunta, **Publicar** y abrir la página de respuesta.
3. Responderla con otro usuario (que no sea administrador) y revisar **Resultados**.
4. Con un usuario que **no** es administrador, abrir `/Encuestas` → debe recibir *403 No tienes permisos*.
5. Eliminar la encuesta de prueba (solo se puede si no tiene respuestas; si las tiene, archivarla).

## 6. Recomendaciones
- **HTTPS**: publicar el sitio solo por HTTPS (certificado interno del dominio).
- **Sin internet**: Bootstrap, jQuery y Bootstrap Icons van incluidos en `Areas\Encuestas\Content\lib` (ver `LEEME.txt`);
  el sitio no hace ninguna petición fuera del servidor.
- **Respaldos**: incluir `PortalEncuestas` en los respaldos de SQL Server.
- **Actualizaciones**: volver a ejecutar `tools\publish.ps1` y reemplazar los archivos de la carpeta
  (IIS reinicia la aplicación sola). Los cambios de base de datos vienen como `Database\upgrade-NNN-*.sql`;
  se aplican todos (en orden, se pueden repetir) con `tools\setup-db.ps1 -Server "..." -NoSamples` sobre la base existente.

## Problemas frecuentes
| Síntoma | Causa probable |
|---|---|
| Pide usuario y contraseña en cada página | El sitio no está en la zona "Intranet local" del navegador; agregarlo por directiva de grupo. |
| "La página expiró por seguridad" al guardar | Autenticación anónima habilitada en IIS (paso 3.4). |
| 403 *No tienes permisos para administrar encuestas* | El usuario no está en `Surveys:AdminRoles`, o el grupo está mal escrito (`DOMINIO\Grupo`). |
| Error 500 al abrir el listado | Sin conexión o sin permisos en SQL Server (paso 4). Ver el Visor de eventos → Aplicación (origen ASP.NET). |
| La segmentación no muestra roles ni sucursales | Las tablas `Org*` están vacías (paso 1). |
| Los acentos se ven mal | Se editó una vista `.cshtml` y se guardó sin UTF-8; guardarla como "UTF-8 con firma". |
