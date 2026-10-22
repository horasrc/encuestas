<#
  esquema-sql.ps1
  Genera los scripts de instalación (para ejecutarlos en SSMS) con las tablas del módulo
  en OTRO esquema en lugar de "enc"; por ejemplo uno que el portal ya tiene, cuando no se
  permite crear esquemas nuevos.

      powershell -ExecutionPolicy Bypass -File tools\esquema-sql.ps1 -Esquema IC

  Resultado: Database\esquema-IC\ con schema.sql, org-schema.sql, org-vistas-plantilla.sql y
  los upgrade-*.sql (en UTF-8 con BOM para que SSMS respete los acentos).
  En el Web.config del portal: <add key="Surveys:Schema" value="IC" />
#>
param([Parameter(Mandatory = $true)][string]$Esquema)
$ErrorActionPreference = 'Stop'
if ($Esquema -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') { throw "Nombre de esquema no válido: '$Esquema' (solo letras, números y _)." }
if ($Esquema -eq 'enc') { throw 'Los scripts de Database\ ya usan el esquema enc.' }

$root = Split-Path -Parent $PSScriptRoot
$db = Join-Path $root 'Database'
$out = Join-Path $db "esquema-$Esquema"
New-Item -ItemType Directory -Force $out | Out-Null
$utf8Bom = New-Object Text.UTF8Encoding($true)

# upgrade-003 mueve tablas antiguas de dbo a enc y los seed son datos de ejemplo: no aplican.
$files = @('schema.sql', 'org-schema.sql', 'org-vistas-plantilla.sql') +
    (Get-ChildItem $db -Filter 'upgrade-*.sql' | Where-Object Name -ne 'upgrade-003-esquema-enc.sql' | Sort-Object Name | ForEach-Object Name)

foreach ($f in $files) {
    $text = [IO.File]::ReadAllText((Join-Path $db $f), [Text.Encoding]::UTF8)
    # "enc" solo aparece como nombre del esquema (enc.Tabla, SCHEMA_ID('enc'), SCHEMA::enc, textos y comentarios).
    $text = [regex]::Replace($text, '\benc\b', $Esquema)
    $header = "-- GENERADO por tools\esquema-sql.ps1 para el esquema [$Esquema] a partir de Database\$f. No editar a mano.`r`n"
    [IO.File]::WriteAllText((Join-Path $out $f), $header + $text, $utf8Bom)
    Write-Host "Generado: Database\esquema-$Esquema\$f"
}
Write-Host ''
Write-Host "En el Web.config del portal agrega: <add key=""Surveys:Schema"" value=""$Esquema"" />" -ForegroundColor Green
