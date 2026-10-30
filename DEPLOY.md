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

Crea el esquema `enc` con las tablas de encuestas (`schema.sql`), **sin datos de ejemplo**.
Alternativa: ejecutar ese script en SQL Server Management Studio. No hay tablas de empleados ni de
sucursales: quién administra y el tramo de control los indica el portal (INTEGRACION.md, paso 3).

## 2. Generar el paquete
En el equipo de desarrollo:

    powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -SqlServer "SQLPROD01\PORTAL"

- El módulo **no tiene administración de usuarios**: sin `SurveyAccess.Resolver` (lo asigna el portal),
  cualquier usuario que entre al sitio puede crear, editar, publicar y ver resultados (ver INTEGRACION.md, paso 3).
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
   - **Autenticación anónima: Deshabilitada** ← importante: con acceso anónimo, el módulo no
     recibe el usuario (bitácora, "ya respondió" y encuestas que exigen autenticación).
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
1. Abrir `https://servidor/Encuestas` → debe verse el listado.
2. Crear una encuesta de prueba, agregar una pregunta, **Publicar** y abrir la página de respuesta.
3. Responderla con otro usuario y revisar **Resultados**.
4. Eliminar la encuesta de prueba (solo se puede si no tiene respuestas; si las tiene, archivarla).

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
| Error 500 al abrir el listado | Sin conexión o sin permisos en SQL Server (paso 4). Ver el Visor de eventos → Aplicación (origen ASP.NET). |
| Un usuario no ve la encuesta o recibe 403 | Su segmento o su permiso de administrar no vienen en `SurveyAccess.Resolver` del portal. |
| La vista parcial de Radar sale en blanco | El portal envía `X-Frame-Options: DENY`; debe ser `SAMEORIGIN`. |
| Los acentos se ven mal | Se editó una vista `.cshtml` y se guardó sin UTF-8; guardarla como "UTF-8 con firma". |
