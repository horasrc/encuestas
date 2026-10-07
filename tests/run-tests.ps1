<#
  run-tests.ps1
  Compila y ejecuta las pruebas de reglas de negocio (tests\ServiceTests.cs)
  contra Services\ y Models\Dto\ usando el compilador de .NET Framework,
  sin Visual Studio, NuGet ni base de datos (usa InMemorySurveyRepository).

      powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1

  Cubre: versionado, publicación, lógica condicional en servidor, validaciones,
  una respuesta por usuario, audiencia, elegibilidad, duplicar y eliminar.
  Sale con código 1 si alguna prueba falla.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }

$out = Join-Path ([IO.Path]::GetTempPath()) 'EncuestasServiceTests'
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'ServiceTests.exe'

$sources = @(Join-Path $PSScriptRoot 'ServiceTests.cs') +
    (Get-ChildItem (Join-Path $root 'Encuestas.Web\Models\Dto') -Filter *.cs).FullName +
    (Get-ChildItem (Join-Path $root 'Encuestas.Web\Services') -Filter *.cs).FullName

& $csc /nologo /warn:4 "/out:$exe" /r:System.Core.dll /r:System.Data.dll /r:System.Configuration.dll /r:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { Write-Host 'Error de compilación.' -ForegroundColor Red; exit 1 }

$output = & $exe
$output | ForEach-Object { Write-Host $_ -ForegroundColor $(if ($_ -like 'FAIL*') { 'Red' } elseif ($_ -like 'OK*') { 'Gray' } else { 'Green' }) }
if ($output -match '^FAIL') { exit 1 }
