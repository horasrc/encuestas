<#
  build-demo.ps1
  Genera las páginas de demostración estáticas a partir de las vistas Razor,
  para revisar el módulo en un navegador sin Visual Studio ni IIS:

      Encuestas.Web\demo-index.html      <- Areas\Encuestas\Views\Encuestas\Index.cshtml       (listado)
      Encuestas.Web\demo.html            <- Areas\Encuestas\Views\Encuestas\Constructor.cshtml (constructor, ?id=s1)
      Encuestas.Web\demo-responder.html  <- Areas\Encuestas\Views\Encuestas\Responder.cshtml   (colaborador, ?id=s1)

  Las vistas son la fuente de verdad: después de editarlas, vuelve a ejecutar:

      powershell -ExecutionPolicy Bypass -File tools\build-demo.ps1

  Transformaciones:
    - @Html.Partial("_X")                 -> contenido de Areas\Encuestas\Views\Encuestas\_X.cshtml
    - @Html.Partial("_SurveyConfig", ...) -> configuración mock fija (sin token antifalsificación)
    - @Url.Content("~/ruta")              -> ruta relativa
    - @Url.Action("Index", "Encuestas")   -> demo-index.html
    - Bloques entre @* demo:body *@ / @* demo:end-body *@ y demo:scripts
#>
$ErrorActionPreference = 'Stop'
$root  = Split-Path -Parent $PSScriptRoot
$web   = Join-Path $root 'Encuestas.Web'
$views = Join-Path $web 'Areas\Encuestas\Views'
$utf8  = New-Object System.Text.UTF8Encoding $false

function Read-Text([string]$path) { [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) }

$demoConfig = @'
<script>
    // Configuración de la demo estática (equivale a _SurveyConfig.cshtml con UseMockData = true).
    window.SURVEY_CONFIG = {
        surveyId: new URLSearchParams(window.location.search).get('id') || 's1',
        useMock: true,
        currentUser: '',
        urls: { index: 'demo-index.html', constructor: 'demo.html?id=__ID__', respond: 'demo-responder.html?id=__ID__' }
    };
</script>
'@

function Expand-Razor([string]$text) {
    $text = [regex]::Replace($text, '@Html\.Partial\("(_\w+)"(?:\s*,\s*Model)?\)', {
        param($m)
        $name = $m.Groups[1].Value
        if ($name -eq '_SurveyConfig') { return $demoConfig }
        if ($name -eq '_ModuleStyles' -or $name -eq '_ModuleScripts') { return '' }   # solo aplican con el layout del portal
        $file = Join-Path $views ("Encuestas\" + $name + '.cshtml')
        if (-not (Test-Path $file)) { throw "No existe la vista parcial $file" }
        return Expand-Razor (Read-Text $file)
    })
    $text = [regex]::Replace($text, '@\*[\s\S]*?\*@\r?\n?', '')
    $text = [regex]::Replace($text, '@Url\.(?:Content|Versioned)\("~/([^"]+)"\)', '$1')
    # Menú del layout: en la demo solo existe Encuestas (siempre activa); Citas necesita el servidor.
    $text = [regex]::Replace($text, '[ \t]*@\{ var areaActual = [^\r\n]*\}\r?\n', '')
    $text = [regex]::Replace($text, '<a class="nav-link@\(areaActual == "Encuestas"[^\r\n]*?>', '<a class="nav-link active" aria-current="page" href="demo-index.html">')
    $text = [regex]::Replace($text, '[ \t]*<a class="nav-link@\(areaActual == "Citas"[^\r\n]*</a>\r?\n', '')
    $text = $text.Replace('@Url.Action("Index", "Encuestas", new { area = "Encuestas" })', 'demo-index.html')
    # "Por responder" y el modo marco (embed) solo existen con el servidor y el portal.
    $text = [regex]::Replace($text, '[ \t]*<a [^>]*@Url\.Action\("Pendientes"[\s\S]*?</a>\r?\n', '')
    $text = [regex]::Replace($text, '[ \t]*@if \(Model\.Embedded\)\r?\n[ \t]*\{[\s\S]*?\r?\n[ \t]*\}\r?\n', '')
    return $text
}

function Between([string]$text, [string]$start, [string]$end) {
    $i = $text.IndexOf($start); $j = $text.IndexOf($end)
    if ($i -lt 0 -or $j -lt 0) { throw "No se encontraron los marcadores $start / $end" }
    return $text.Substring($i + $start.Length, $j - $i - $start.Length)
}

$banner = '<!-- ARCHIVO GENERADO por tools/build-demo.ps1 a partir de Areas/Encuestas/Views/. No editar a mano. -->'

function Assert-NoRazor([string]$html, [string]$name) {
    if ($html -match '@(Html|Url|Model|RenderBody|RenderSection|ViewBag)') { throw "Quedó sintaxis Razor sin convertir en ${name}: $($Matches[0])" }
}

function Save([string]$html, [string]$name) {
    $html = $html -replace '<!DOCTYPE html>', ("<!DOCTYPE html>`r`n" + $banner)
    Assert-NoRazor $html $name
    [IO.File]::WriteAllText((Join-Path $web $name), $html, $utf8)
    Write-Host "Generado: Encuestas.Web\$name"
}

# Páginas que usan _Layout.cshtml
$layout = Read-Text (Join-Path $views 'Shared\_LayoutEncuestas.cshtml')
$pages = @(
    @{ View = 'Index.cshtml';       Out = 'demo-index.html'; Title = 'Encuestas' },
    @{ View = 'Constructor.cshtml'; Out = 'demo.html';       Title = 'Constructor de encuestas' }
)
foreach ($p in $pages) {
    $view    = Read-Text (Join-Path $views ("Encuestas\" + $p.View))
    $body    = Expand-Razor (Between $view '@* demo:body *@' '@* demo:end-body *@')
    $scripts = Expand-Razor (Between $view '@* demo:scripts *@' '@* demo:end-scripts *@')
    $html = $layout.Replace('@ViewBag.Title', $p.Title)
    $html = $html.Replace('@RenderBody()', $body.Trim())
    $html = $html.Replace('@RenderSection("scripts", required: false)', $scripts.Trim())
    Save (Expand-Razor $html) $p.Out
}

# Página del colaborador (sin layout)
$respond = Read-Text (Join-Path $views 'Encuestas\Responder.cshtml')
$respond = $respond.Substring($respond.IndexOf('<!DOCTYPE html>'))
Save (Expand-Razor $respond) 'demo-responder.html'
