<#
  e2e-tests.ps1
  Pruebas de integración contra el sitio en ejecución (IIS Express + SQL Server):
  autenticación de Windows, guardado y publicación en SQL,
  lógica condicional en servidor, una respuesta por usuario, resultados, versionado,
  crear / duplicar / eliminar.

  Requisitos: sitio corriendo en http://localhost:51842 con UseMockData = false,
  Repository = Sql, y la base recién creada:
      powershell -ExecutionPolicy Bypass -File tools\setup-db.ps1 -Recreate
      powershell -ExecutionPolicy Bypass -File tests\e2e-tests.ps1

  ¡Modifica datos! (publica la encuesta 1, envía una respuesta, crea versiones). Solo desarrollo.
#>
param([string]$Base = 'http://localhost:51842')
$ErrorActionPreference = 'Stop'
$base = $Base.TrimEnd('/')
$fails = 0
function Check($name, $ok, $detail) { if ($ok) { Write-Host "OK   $name" } else { Write-Host "FAIL $name -> $detail" -ForegroundColor Red; $script:fails++ } }

# Sesión como el navegador: autenticación Windows + cookies (HttpWebRequest para leer también cuerpos de error)
$jar = New-Object Net.CookieContainer
function Req($method, $url, $body, $headers) {
    $q = [Net.HttpWebRequest]::Create($url); $q.Method = $method; $q.CookieContainer = $jar; $q.UseDefaultCredentials = $true
    if ($headers) { foreach ($k in $headers.Keys) { $q.Headers[$k] = $headers[$k] } }
    if ($body) { $q.ContentType = 'application/json; charset=utf-8'; $b = [Text.Encoding]::UTF8.GetBytes($body); $q.ContentLength = $b.Length; $st = $q.GetRequestStream(); $st.Write($b, 0, $b.Length); $st.Close() }
    try { $r = $q.GetResponse() } catch [Net.WebException] { $r = $_.Exception.Response; if (-not $r) { throw } }
    $txt = (New-Object IO.StreamReader($r.GetResponseStream(), [Text.Encoding]::UTF8)).ReadToEnd(); $code = [int]$r.StatusCode; $r.Close()
    return @{ code = $code; body = $txt }
}
$page = Req GET "$base/Encuestas/Constructor/1"
Check 'Config en modo servidor' ($page.body -match 'useMock: false') 'useMock no es false'
$anon = [Net.HttpWebRequest]::Create("$base/Encuestas"); try { $anon.GetResponse().Close(); $anonCode = 200 } catch [Net.WebException] { $anonCode = [int]$_.Exception.Response.StatusCode }
Check 'Sin credenciales de Windows: 401' ($anonCode -eq 401) "HTTP $anonCode"

function Api($method, $path, $body) {
    $headers = @{ 'X-Requested-With' = 'XMLHttpRequest' }
    $json = if ($body) { $body | ConvertTo-Json -Depth 30 } else { $null }
    $r = Req $method "$base$path" $json $headers
    try { return $r.body | ConvertFrom-Json } catch { return @{ ok = $false; message = "HTTP $($r.code): $($r.body.Substring(0, [Math]::Min(300, $r.body.Length)))" } }
}

# 1. Cargar, editar y guardar el borrador (escritura en SQL)
$survey = Api GET '/Encuestas/Api/Survey/1'
$v1 = $survey.versions[0]
Check 'Encuesta 1 cargada desde SQL' ($v1.questions.Count -eq 7) "preguntas=$($v1.questions.Count)"
$v1.questions[0].text = '¿Qué tan útil fue el material de la campaña? (editado)'
$v1.settings.startDate = (Get-Date).ToString('yyyy-MM-dd')
$r = Api POST '/Encuestas/Api/SaveDraft' @{ surveyId = '1'; name = $survey.name; version = $v1 }
Check 'SaveDraft (POST)' $r.ok $r.message
$reload = Api GET '/Encuestas/Api/Survey/1'
Check 'Cambio persistido en SQL' ($reload.versions[0].questions[0].text -like '*(editado)') $reload.versions[0].questions[0].text
$q4 = $reload.versions[0].questions | ? { $_.rules.Count -gt 0 }
Check 'Reglas condicionales conservadas tras reescribir' ($q4.rules.Count -eq 3 -and ($q4.rules | ? { $_.action -eq 'show' }).Count -eq 2) "reglas=$($q4.rules.Count)"
$dep = $reload.versions[0].questions | ? { $_.type -eq 'dependent' }
Check 'Árbol dependiente conservado' ($dep.dependent.options.Count -eq 17 -and $dep.dependent.levels.Count -eq 3) "opciones=$($dep.dependent.options.Count)"

# 2. Publicar
$r = Api POST '/Encuestas/Api/Publish' @{ surveyId = '1'; versionId = $reload.versions[0].id }
Check 'Publish' $r.ok "$($r.message) $($r.errors -join '; ')"

# 4. Elegibilidad y envío como colaborador (el seed exige autenticación y 1 respuesta por usuario)
$e = Api GET '/Encuestas/Api/Eligibility/1'
Check 'Eligibility: puede responder' $e.canRespond "$($e.code) $($e.reason)"
$pub = Api GET '/Encuestas/Api/Published/1'
$pv = $pub.versions[0]
Check 'Published sin segmentación ni histórico' ($pub.versions.Count -eq 1 -and -not $pv.segmentation) 'expone datos de más'
$byKey = @{}; $pv.questions | % { $byKey[$_.id] = $_ }
$bad = Api POST '/Encuestas/Api/Submit' @{ surveyId = '1'; versionId = $pv.id; answers = @{ q1 = @{ optionIds = @('q1o1') }; q4 = @{ optionIds = @('q4o1') } } }
Check 'Submit "Sí" sin P5 rechazado por el servidor' (-not $bad.ok -and ($bad.errors -join ' ') -match 'P5') "$($bad.message) $($bad.errors -join '; ')"
$answers = @{
    q1 = @{ optionIds = @('q1o2') }
    q2 = @{ optionIds = @('q2o1', '__other__'); otherText = 'Material de WhatsApp' }
    q3 = @{ text = 'Más ejemplos prácticos y casos reales, por favor.' }
    q4 = @{ optionIds = @('q4o1') }
    q5 = @{ optionIds = @('q5d1', 'q5d2', 'q5d3') }
    q6 = @{ optionIds = @('q6o1') }   # oculta (P4 = Sí): debe descartarse
    q7 = @{ matrix = @{ q7r1 = @('q7c4'); q7r2 = @('q7c5'); q7r3 = @('q7c3'); q7r4 = @('q7c4') } }
}
$r = Api POST '/Encuestas/Api/Submit' @{ surveyId = '1'; versionId = $pv.id; answers = $answers }
Check 'Submit válido' $r.ok "$($r.message) $($r.errors -join '; ')"
$e = Api GET '/Encuestas/Api/Eligibility/1'
Check 'Eligibility: ya respondió' (-not $e.canRespond -and $e.code -eq 'alreadyResponded') $e.code
$r = Api POST '/Encuestas/Api/Submit' @{ surveyId = '1'; versionId = $pv.id; answers = $answers }
Check 'Segundo envío rechazado' (-not $r.ok) $r.message

# 5. Resultados leídos desde SQL
$res = Api GET "/Encuestas/Api/Results/$($pv.id)?surveyId=1"
$a = $res.responses[0].answers
Check 'Resultados: 1 respuesta' ($res.responses.Count -eq 1) "n=$($res.responses.Count)"
Check 'Resultados: "Otro" con texto' (($a.q2.optionIds -contains '__other__') -and $a.q2.otherText -eq 'Material de WhatsApp') ($a.q2 | ConvertTo-Json -Compress)
Check 'Resultados: matriz' ($a.q7.matrix.q7r2 -contains 'q7c5') ($a.q7 | ConvertTo-Json -Compress)
Check 'Resultados: dependiente' (@($a.q5.optionIds).Count -eq 3) ($a.q5 | ConvertTo-Json -Compress)
Check 'Resultados: pregunta oculta descartada' (-not $a.q6) 'q6 guardada'
Check 'Resultados: sin tramo de control del portal, audiencia 0' ($res.audience -eq 0) "audiencia=$($res.audience)"
$cat = Req GET "$base/Encuestas/Api/Catalogs" $null @{ 'X-Requested-With' = 'XMLHttpRequest' }
Check 'Ya no existe la API de catálogos (segmentación propia)' ($cat.code -eq 404) "HTTP $($cat.code)"

# 6. Versionado: la publicada con respuestas no se sobrescribe
$cur = Api GET '/Encuestas/Api/Survey/1'
$pubV = $cur.versions | ? { $_.status -eq 'Published' }
Check 'ResponseCount = 1' ($pubV.responseCount -eq 1) $pubV.responseCount
$pubV.questions[0].text = 'Cambio que NO debe sobrescribir la v1'
$r = Api POST '/Encuestas/Api/SaveDraft' @{ surveyId = '1'; name = $cur.name; version = $pubV }
$after = Api GET '/Encuestas/Api/Survey/1'
$v1Now = $after.versions | ? { $_.number -eq 1 }; $v2 = $after.versions | ? { $_.number -eq 2 }
Check 'Guardar sobre v1 con respuestas crea v2 borrador' ($r.ok -and $v2.status -eq 'Draft') $r.message
Check 'v1 intacta' ($v1Now.questions[0].text -like '*(editado)') $v1Now.questions[0].text
Check 'v2 con el cambio y reglas reapuntadas' ($v2.questions[0].text -like 'Cambio*' -and (($v2.questions | ? { $_.rules.Count }).rules | ? { $_.targetQuestionId } | % { $_.targetQuestionId } | ? { $v2.questions.id -contains $_ }).Count -eq 2) 'reglas no reapuntadas'

# 7. Publicar v2 -> v1 Archivada
$r = Api POST '/Encuestas/Api/Publish' @{ surveyId = '1'; versionId = $v2.id }
$after = Api GET '/Encuestas/Api/Survey/1'
Check 'Publicar v2 archiva v1 (índices únicos respetados)' ($r.ok -and ($after.versions | ? { $_.number -eq 1 }).status -eq 'Archived' -and ($after.versions | ? { $_.number -eq 2 }).status -eq 'Published') $r.message

# 8. Crear, duplicar y eliminar
$c = Api POST '/Encuestas/Api/Create' @{ name = 'Prueba E2E'; description = 'creada por e2e' }
Check 'Create' ($c.ok -and $c.data.id) $c.message
$d = Api POST '/Encuestas/Api/Duplicate' @{ surveyId = '1' }
Check 'Duplicate (copia v2 publicada como borrador)' ($d.ok -and $d.data.versions[0].questions.Count -eq 7) $d.message
$x = Api POST '/Encuestas/Api/Delete' @{ surveyId = $c.data.id }
Check 'Delete sin respuestas' $x.ok $x.message
$x = Api POST '/Encuestas/Api/Delete' @{ surveyId = '1' }
Check 'Delete con respuestas rechazado' (-not $x.ok) $x.message
$list = Api GET '/Encuestas/Api/List'
Check 'Listado final (1 + copia)' (@($list).Count -eq 2) "n=$(@($list).Count)"

Write-Host ''
if ($fails) { Write-Host "$fails FALLAS" -ForegroundColor Red } else { Write-Host 'TODAS LAS PRUEBAS E2E PASARON' -ForegroundColor Green }
