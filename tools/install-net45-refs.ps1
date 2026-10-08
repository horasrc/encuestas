<#
  install-net45-refs.ps1
  Instala los ensamblados de referencia de .NET Framework 4.5 para que Visual Studio 2022
  cargue el proyecto sin pedir "Actualizar el destino a .NET Framework 4.8".

  Visual Studio 2022 ya no instala el paquete de destino 4.5. Este script copia los archivos
  del paquete NuGet oficial de Microsoft (Microsoft.NETFramework.ReferenceAssemblies.net45,
  ya incluido en packages\ al restaurar NuGet) a:
      C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5

  - Solo AGREGA archivos; no borra ni reemplaza lo que ya exista.
  - Requiere PowerShell como ADMINISTRADOR (la carpeta está en Program Files).
  - Solo afecta la compilación en este equipo. El sitio sigue siendo .NET 4.5.

      powershell -ExecutionPolicy Bypass -File tools\install-net45-refs.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'packages\Microsoft.NETFramework.ReferenceAssemblies.net45.1.0.3\build\.NETFramework\v4.5'
$dst = Join-Path ${env:ProgramFiles(x86)} 'Reference Assemblies\Microsoft\Framework\.NETFramework\v4.5'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'Este script necesita permisos de administrador.' -ForegroundColor Yellow
    Write-Host 'Abre PowerShell con clic derecho > "Ejecutar como administrador" y vuelve a ejecutarlo.'
    exit 1
}
if (-not (Test-Path (Join-Path $src 'RedistList\FrameworkList.xml'))) {
    throw "No se encontró el paquete en $src. Restaura los paquetes NuGet de la solución primero."
}

$copied = 0; $skipped = 0
Get-ChildItem $src -Recurse -File | ForEach-Object {
    $rel = $_.FullName.Substring($src.Length + 1)
    $target = Join-Path $dst $rel
    if (Test-Path $target) { $skipped++; return }
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $_.FullName $target
    $copied++
}

$ok = (Test-Path (Join-Path $dst 'RedistList\FrameworkList.xml')) -and (Test-Path (Join-Path $dst 'mscorlib.dll'))
Write-Host ("Archivos copiados: {0}   (ya existían: {1})" -f $copied, $skipped)
if ($ok) {
    Write-Host 'Listo: .NET Framework 4.5 instalado como destino de compilación.' -ForegroundColor Green
    Write-Host 'Cierra Visual Studio por completo y vuelve a abrir Encuestas.sln.'
} else {
    throw 'La instalación no quedó completa (falta RedistList\FrameworkList.xml o mscorlib.dll).'
}
