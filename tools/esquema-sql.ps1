<#
  esquema-sql.ps1
  Genera los scripts de instalación (para ejecutarlos en SSMS) con las tablas del módulo
  en OTRO esquema en lugar de "enc"; por ejemplo uno que el portal ya tiene, cuando no se
  permite crear esquemas nuevos.

      powershell -ExecutionPolicy Bypass -File tools\esquema-sql.ps1 -Esquema IC

  Resultado: Database\esquema-IC\ con schema.sql y los upgrade-*.sql (en UTF-8 con BOM para
  que SSMS respete los acentos). Ya no hay tablas Org*: quién responde lo decide el portal.
  En el Web.config del portal: <add key="Surveys:Schema" value="IC" />

  -SinLlavesForaneas: quita las llaves foráneas (FOREIGN KEY) entre las tablas del módulo.
  Para cuando se pueden crear tablas en el esquema pero no se tiene el permiso REFERENCES
  (error "The REFERENCES permission was denied"). Resultado en Database\esquema-IC-sin-FK\.
  El módulo funciona igual (guarda y borra en orden dentro de transacciones); lo que se pierde
  es que la base impida datos inconsistentes si alguien edita las tablas a mano.
#>
param(
    [Parameter(Mandatory = $true)][string]$Esquema,
    [switch]$SinLlavesForaneas
)
$ErrorActionPreference = 'Stop'
if ($Esquema -notmatch '^[A-Za-z_][A-Za-z0-9_]{0,127}$') { throw "Nombre de esquema no válido: '$Esquema' (solo letras, números y _)." }
if ($Esquema -eq 'enc') { throw 'Los scripts de Database\ ya usan el esquema enc.' }

$root = Split-Path -Parent $PSScriptRoot
$db = Join-Path $root 'Database'
$carpeta = "esquema-$Esquema" + $(if ($SinLlavesForaneas) { '-sin-FK' } else { '' })
$out = Join-Path $db $carpeta
New-Item -ItemType Directory -Force $out | Out-Null
$utf8Bom = New-Object Text.UTF8Encoding($true)

# upgrade-003 mueve tablas antiguas de dbo a enc y los seed son datos de ejemplo: no aplican.
$files = @('schema.sql') +
    (Get-ChildItem $db -Filter 'upgrade-*.sql' | Where-Object Name -ne 'upgrade-003-esquema-enc.sql' | Sort-Object Name | ForEach-Object Name)

foreach ($f in $files) {
    $text = [IO.File]::ReadAllText((Join-Path $db $f), [Text.Encoding]::UTF8)
    # "enc" solo aparece como nombre del esquema (enc.Tabla, SCHEMA_ID('enc'), SCHEMA::enc, textos y comentarios).
    $text = [regex]::Replace($text, '\benc\b', $Esquema)
    $header = "-- GENERADO por tools\esquema-sql.ps1 para el esquema [$Esquema] a partir de Database\$f. No editar a mano.`r`n"
    if ($SinLlavesForaneas) {
        # " CONSTRAINT FK_x REFERENCES esquema.Tabla(Columna) [ON DELETE CASCADE]" en la definición de la columna.
        $text = [regex]::Replace($text, '\s+CONSTRAINT\s+FK_\w+\s+REFERENCES\s+[\w\[\].]+\s*\(\w+\)(\s+ON\s+DELETE\s+CASCADE)?', '')
        if ($text -match 'REFERENCES\s') { throw "Quedó una llave foránea sin quitar en $f." }
        $header += "-- SIN LLAVES FORÁNEAS: para instalar sin el permiso REFERENCES. La integridad entre tablas la mantiene el módulo.`r`n"
    }
    [IO.File]::WriteAllText((Join-Path $out $f), $header + $text, $utf8Bom)
    Write-Host "Generado: Database\$carpeta\$f"
}
Write-Host ''
Write-Host "En el Web.config del portal agrega: <add key=""Surveys:Schema"" value=""$Esquema"" />" -ForegroundColor Green
