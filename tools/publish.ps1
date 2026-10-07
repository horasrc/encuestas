<#
  publish.ps1
  Genera la carpeta lista para copiar al servidor IIS (configuración Release):
    - restaura NuGet, compila, precompila las vistas Razor
    - aplica Web.Release.config (sin debug, modo servidor, SQL)
    - escribe el servidor SQL y los grupos administradores en el Web.config publicado

      powershell -ExecutionPolicy Bypass -File tools\publish.ps1 `
          -SqlServer "SQLPROD01\PORTAL" -AdminRoles "MIDOMINIO\Encuestas-Admin"

  Parámetros:
    -SqlServer   servidor\instancia de SQL Server de producción (obligatorio)
    -AdminRoles  grupo(s) de Windows que administran encuestas, separados por coma (obligatorio)
    -Database    nombre de la base (por defecto PortalEncuestas)
    -AllowLocalDb  solo para probar la carpeta publicada en el equipo de desarrollo

  Resultado: publish\Encuestas.Web  (ver DEPLOY.md para instalarlo en IIS)
#>
param(
    [Parameter(Mandatory = $true)][string]$SqlServer,
    [Parameter(Mandatory = $true)][string]$AdminRoles,
    [string]$Database = 'PortalEncuestas',
    [switch]$AllowLocalDb
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'publish\Encuestas.Web'

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msbuild = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'No se encontró MSBuild. Instala Visual Studio con la carga "Desarrollo de ASP.NET y web".' }

Push-Location $root
try {
    Write-Host 'Restaurando paquetes NuGet...'
    & $msbuild Encuestas.sln /t:restore /p:RestorePackagesConfig=true /nologo /v:quiet
    if ($LASTEXITCODE) { throw 'Falló la restauración de NuGet.' }

    Write-Host 'Compilando y publicando (Release)...'
    & $msbuild Encuestas.Web\Encuestas.Web.csproj /p:Configuration=Release /p:DeployOnBuild=true /p:PublishProfile=Carpeta /nologo /v:minimal
    if ($LASTEXITCODE) { throw 'Falló la compilación o la publicación.' }
} finally { Pop-Location }

# Completar los marcadores del Web.config publicado.
$config = Join-Path $out 'Web.config'
$xml = [IO.File]::ReadAllText($config, [Text.Encoding]::UTF8)
$esc = { param($v) [Security.SecurityElement]::Escape($v) }
$xml = $xml.Replace('__SQL_SERVER__', (& $esc $SqlServer)).Replace('__DATABASE__', (& $esc $Database)).Replace('__ADMIN_ROLES__', (& $esc $AdminRoles))
[IO.File]::WriteAllText($config, $xml, (New-Object Text.UTF8Encoding $true))

# Verificaciones.
$problems = @()
if ($xml -match '__[A-Z_]+__') { $problems += 'Quedaron marcadores sin reemplazar en Web.config.' }
if ($xml -match 'debug="true"') { $problems += 'Web.config publicado tiene debug="true".' }
if (-not $AllowLocalDb -and $xml -match 'connectionString="[^"]*localdb') { $problems += 'La cadena de conexión publicada apunta a LocalDB.' }
if ($xml -notmatch 'Surveys:UseMockData" value="false"') { $problems += 'UseMockData no quedó en false.' }
foreach ($f in 'demo.html', 'demo-index.html', 'demo-responder.html', 'Web.Release.config') {
    if (Test-Path (Join-Path $out $f)) { $problems += "No debería publicarse: $f" }
}
if (-not (Test-Path (Join-Path $out 'bin\Encuestas.Web.dll'))) { $problems += 'Falta bin\Encuestas.Web.dll.' }

if ($problems) { $problems | ForEach-Object { Write-Host "  ✗ $_" -ForegroundColor Red }; throw 'La publicación no pasó las verificaciones.' }

$size = (Get-ChildItem $out -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ''
Write-Host ("Publicación lista: {0}  ({1:N1} MB)" -f $out, $size) -ForegroundColor Green
Write-Host "  SQL Server:     $SqlServer / $Database"
Write-Host "  Administradores: $AdminRoles"
Write-Host 'Siguiente paso: copiar la carpeta al servidor y seguir DEPLOY.md.'
