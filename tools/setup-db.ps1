<#
  setup-db.ps1
  Crea las tablas del módulo de encuestas (esquema "enc") y aplica las actualizaciones.
  No requiere sqlcmd: usa System.Data.SqlClient y separa los lotes por "GO".

  Desarrollo (LocalDB, base propia con datos de ejemplo):
      powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1

  En la base del PORTAL (solo crea el esquema enc y sus tablas; no toca nada del portal):
      powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Server "SQLPROD01\PORTAL" -Database BasePortal -NoSamples
      (agrega -NoOrgTables si los catálogos de la organización serán vistas sobre tablas del portal)

  Si las tablas ya existen, solo aplica Database\upgrade-*.sql (todas son re-ejecutables).

  -Recreate borra y vuelve a crear la base COMPLETA. Solo se permite con LocalDB, para
  que nunca se pueda usar contra la base del portal.

  -Schema IC crea las tablas en ese esquema en lugar de "enc" (debe coincidir con
  Surveys:Schema en el Web.config). -SinLlavesForaneas las crea sin FOREIGN KEY
  (igual que tools\esquema-sql.ps1 -SinLlavesForaneas).
#>
param(
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$Database = 'PortalEncuestas',
    [switch]$NoSamples,
    [switch]$NoOrgTables,
    [switch]$Recreate,
    [string]$Schema = 'enc',
    [switch]$SinLlavesForaneas
)
$ErrorActionPreference = 'Stop'
if ($Schema -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') { throw "Nombre de esquema no válido: '$Schema'." }
$root = Split-Path -Parent $PSScriptRoot
$db = Join-Path $root 'Database'
$isLocalDb = $Server -like '(localdb)*'

if ($Recreate -and -not $isLocalDb) {
    throw "-Recreate borra la base completa y solo se permite con LocalDB. Contra '$Server' no se usa: el módulo vive dentro de la base del portal."
}

if ($isLocalDb) {
    $instance = $Server.Split('\')[1]
    $localdb = Get-ChildItem "$env:ProgramFiles\Microsoft SQL Server\*\Tools\Binn\SqlLocalDB.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $localdb) { throw 'No se encontró SqlLocalDB.exe. Instala el componente "SQL Server Express LocalDB" de Visual Studio.' }
    & $localdb.FullName create $instance 2>$null | Out-Null   # si ya existe, no pasa nada
    & $localdb.FullName start $instance | Out-Null
}

function Invoke-Batches([string]$connectionString, [string]$file) {
    $text = [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)
    if ($Schema -ne 'enc') { $text = [regex]::Replace($text, '\benc\b', $Schema) }   # igual que tools\esquema-sql.ps1
    if ($SinLlavesForaneas) { $text = [regex]::Replace($text, '\s+CONSTRAINT\s+FK_\w+\s+REFERENCES\s+[\w\[\].]+\s*\(\w+\)(\s+ON\s+DELETE\s+CASCADE)?', '') }
    $batches = [regex]::Split($text, '^\s*GO\s*$', [Text.RegularExpressions.RegexOptions]::Multiline -bor [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    $conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $conn.add_InfoMessage({ param($s, $e) Write-Host "    $($e.Message)" -ForegroundColor DarkGray })
    $conn.Open()
    try {
        foreach ($b in $batches) {
            if (-not $b.Trim()) { continue }
            $cmd = $conn.CreateCommand(); $cmd.CommandText = $b; $cmd.CommandTimeout = 300
            $reader = $cmd.ExecuteReader()
            do { while ($reader.Read()) { Write-Host ("    " + (0..($reader.FieldCount - 1) | ForEach-Object { "$($reader.GetName($_))=$($reader.GetValue($_))" }) -join ', ') -ForegroundColor DarkGray } } while ($reader.NextResult())
            $reader.Close()
        }
    } finally { $conn.Close() }
}

function Scalar([string]$connectionString, [string]$sql) {
    $c = New-Object System.Data.SqlClient.SqlConnection $connectionString; $c.Open()
    try { $cmd = $c.CreateCommand(); $cmd.CommandText = $sql; $v = $cmd.ExecuteScalar(); if ($v -is [DBNull]) { $null } else { $v } } finally { $c.Close() }
}

$master = "Data Source=$Server;Initial Catalog=master;Integrated Security=True"
$target = "Data Source=$Server;Initial Catalog=$Database;Integrated Security=True"

$exists = [bool](Scalar $master "SELECT DB_ID(N'$($Database.Replace("'", "''"))')")
if ($exists -and $Recreate) {
    Write-Host "Eliminando la base $Database (LocalDB)..." -ForegroundColor Yellow
    Scalar $master "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database];" | Out-Null
    $exists = $false
}
if (-not $exists) {
    if (-not $isLocalDb) { throw "La base '$Database' no existe en '$Server'. El módulo se instala dentro de una base existente (la del portal)." }
    Write-Host "Creando la base $Database en $Server..."
    Scalar $master "CREATE DATABASE [$Database]" | Out-Null
}

# Bases de desarrollo antiguas: tablas en dbo -> se mueven a enc antes de revisar qué falta.
if ($Schema -eq 'enc' -and (Scalar $target "SELECT OBJECT_ID('dbo.SurveyVersion', 'U')")) {
    Write-Host 'Encontré las tablas del módulo en dbo; las muevo al esquema enc...'
    Invoke-Batches $target (Join-Path $db 'upgrade-003-esquema-enc.sql')
}

$hasSurvey = [bool](Scalar $target "SELECT OBJECT_ID('$Schema.Survey')")
$hasOrg = [bool](Scalar $target "SELECT OBJECT_ID('$Schema.OrgUser')")

$steps = @()
if (-not $hasSurvey) { $steps += 'schema.sql'; if (-not $NoSamples) { $steps += 'seed.sql' } }
else { $steps += (Get-ChildItem $db -Filter 'upgrade-*.sql' | Where-Object { $Schema -eq 'enc' -or $_.Name -ne 'upgrade-003-esquema-enc.sql' } | Sort-Object Name | ForEach-Object Name) }
if (-not $hasOrg -and -not $NoOrgTables) { $steps += 'org-schema.sql'; if (-not $NoSamples) { $steps += 'org-seed.sql' } }

foreach ($s in $steps) {
    Write-Host "Ejecutando $s..."
    Invoke-Batches $target (Join-Path $db $s)
}
if ($hasSurvey) { Write-Host 'Las tablas ya existían; solo se aplicaron actualizaciones.' }
if ($NoOrgTables -and -not $hasOrg) { Write-Host "Sin tablas Org*: crea las vistas $Schema.Org* sobre las tablas del portal (ver INTEGRACION.md)." -ForegroundColor Yellow }

Write-Host ''
Write-Host "Listo. Tablas del módulo en $Database (esquema $Schema)." -ForegroundColor Green
if ($Schema -ne 'enc') { Write-Host "  En el Web.config: <add key=""Surveys:Schema"" value=""$Schema"" />" }
Write-Host "  Cadena de conexión: $target"
