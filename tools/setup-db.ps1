<#
  setup-db.ps1
  Crea la base de datos del módulo de encuestas y ejecuta los scripts de Database\.
  No requiere sqlcmd: usa System.Data.SqlClient y separa los lotes por "GO".

  Desarrollo (LocalDB, con datos de ejemplo):
      powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1

  Otro servidor, solo estructura (sin datos de ejemplo):
      powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Server "MISERVIDOR\SQL" -Database PortalEncuestas -NoSamples

  -Recreate borra y vuelve a crear la base (¡pierde los datos!). Solo para desarrollo.
#>
param(
    [string]$Server = '(localdb)\MSSQLLocalDB',
    [string]$Database = 'PortalEncuestas',
    [switch]$NoSamples,
    [switch]$Recreate
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$db = Join-Path $root 'Database'

if ($Server -like '(localdb)*') {
    $instance = $Server.Split('\')[1]
    $localdb = Get-ChildItem "$env:ProgramFiles\Microsoft SQL Server\*\Tools\Binn\SqlLocalDB.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $localdb) { throw 'No se encontró SqlLocalDB.exe. Instala el componente "SQL Server Express LocalDB" de Visual Studio.' }
    & $localdb.FullName create $instance 2>$null | Out-Null   # si ya existe, no pasa nada
    & $localdb.FullName start $instance | Out-Null
}

function Invoke-Batches([string]$connectionString, [string]$file) {
    $text = [IO.File]::ReadAllText($file, [Text.Encoding]::UTF8)
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

$master = "Data Source=$Server;Initial Catalog=master;Integrated Security=True"
$target = "Data Source=$Server;Initial Catalog=$Database;Integrated Security=True"

$exists = $false
$c = New-Object System.Data.SqlClient.SqlConnection $master; $c.Open()
$cmd = $c.CreateCommand(); $cmd.CommandText = 'SELECT DB_ID(@n)'; [void]$cmd.Parameters.AddWithValue('@n', $Database)
$exists = -not ($cmd.ExecuteScalar() -is [DBNull])
if ($exists -and $Recreate) {
    Write-Host "Eliminando la base $Database..." -ForegroundColor Yellow
    $cmd = $c.CreateCommand(); $cmd.CommandText = "ALTER DATABASE [$Database] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$Database];"; [void]$cmd.ExecuteNonQuery()
    $exists = $false
}
if (-not $exists) {
    Write-Host "Creando la base $Database en $Server..."
    $cmd = $c.CreateCommand(); $cmd.CommandText = "CREATE DATABASE [$Database]"; [void]$cmd.ExecuteNonQuery()
}
$c.Close()

# ¿Ya tiene el esquema?
$c = New-Object System.Data.SqlClient.SqlConnection $target; $c.Open()
$cmd = $c.CreateCommand(); $cmd.CommandText = "SELECT OBJECT_ID('dbo.Survey'), OBJECT_ID('dbo.OrgUser')"
$r = $cmd.ExecuteReader(); [void]$r.Read(); $hasSurvey = -not $r.IsDBNull(0); $hasOrg = -not $r.IsDBNull(1); $r.Close(); $c.Close()

$steps = @()
if (-not $hasSurvey) { $steps += 'schema.sql'; if (-not $NoSamples) { $steps += 'seed.sql' } } else { $steps += 'upgrade-001-audience-mode.sql' }
if (-not $hasOrg) { $steps += 'org-schema.sql'; if (-not $NoSamples) { $steps += 'org-seed.sql' } }

foreach ($s in $steps) {
    Write-Host "Ejecutando $s..."
    Invoke-Batches $target (Join-Path $db $s)
}
if ($hasSurvey -and $hasOrg) { Write-Host 'La base ya tenía todas las tablas; solo se aplicaron actualizaciones.' }

Write-Host ''
Write-Host 'Listo. Cadena de conexión para Web.config (SurveysDb):' -ForegroundColor Green
Write-Host "  $target"
