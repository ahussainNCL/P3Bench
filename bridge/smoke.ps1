# Offline smoke test for the bridge: simulator read -> plan -> apply.
# It starts its own bridge on a spare port, exercises the API with synthetic
# data only, then stops it. No adapter is opened and no vehicle is contacted.
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$port = 8799
$exe = Join-Path $PSScriptRoot "P3OrbitBridge.exe"
if (-not (Test-Path $exe)) { throw "Build the bridge first: bridge\build.cmd" }

$base = "http://127.0.0.1:$port"
$process = Start-Process -FilePath $exe -ArgumentList "--port", $port, "--root", $root -PassThru -WindowStyle Hidden
$failures = 0

function Check($name, [scriptblock]$test) {
  try {
    $result = & $test
    if ($result -eq $false) { throw "assertion returned false" }
    Write-Host ("  ok   " + $name) -ForegroundColor DarkGreen
  } catch {
    $script:failures++
    Write-Host ("  FAIL " + $name + ": " + $_.Exception.Message) -ForegroundColor Red
  }
}

function Api($method, $path, $body) {
  if ($null -eq $body) {
    return Invoke-RestMethod -Method $method -Uri ($base + $path)
  }
  return Invoke-RestMethod -Method $method -Uri ($base + $path) -ContentType "application/json" -Body (ConvertTo-Json -InputObject $body -Depth 8 -Compress)
}

function ApiError($method, $path, $body) {
  try {
    Api $method $path $body | Out-Null
    return $null
  } catch {
    $payload = $_.ErrorDetails.Message
    if (-not $payload) { return @{ code = "http"; message = $_.Exception.Message } }
    return ($payload | ConvertFrom-Json).error
  }
}

try {
  $ready = $false
  for ($i = 0; $i -lt 40 -and -not $ready; $i++) {
    Start-Sleep -Milliseconds 250
    try { Api "GET" "/api/state" $null | Out-Null; $ready = $true } catch { }
  }
  if (-not $ready) { throw "bridge did not start on port $port" }

  Write-Host "`nSimulator flow" -ForegroundColor Cyan
  $state = Api "GET" "/api/state" $null
  Check "starts in simulator mode" { $state.mode -eq "simulator" }
  Check "uses the synthetic demo catalogue" { $state.catalog.synthetic -eq $true -and $state.catalog.configLength -eq 254 }
  Check "no config block before read" { $null -eq $state.dump }

  $read = Api "POST" "/api/read" @{}
  Check "reads 254 bytes" { $read.dump.length -eq 254 }
  Check "checksum validates" { $read.dump.checksum.ok -eq $true }
  Check "parameters are decoded" { $read.catalog.groups[0].params[0].display -ne $null }

  $before = $read.catalog.groups[0].params[0]
  $plan = Api "POST" "/api/plan" @{ edits = @(
      @{ id = $before.id; value = (-not [bool]$before.value) },
      @{ id = "marketCode"; value = "US" }
    ) }
  Check "stages a plan without writing" { $plan.staged.changes.Count -ge 2 -and $plan.dump.hex -eq $read.dump.hex }
  Check "plan includes the checksum when needed" {
    ($plan.staged.changes | Where-Object { $_.kind -eq "checksum" }).Count -le 1
  }

  $bad = ApiError "POST" "/api/apply" @{ planId = $plan.staged.id; confirm = "nope" }
  Check "refuses apply without the confirmation word" { $bad.code -eq "confirm_required" }

  $stale = ApiError "POST" "/api/apply" @{ planId = "plan_deadbeef"; confirm = "APPLY" }
  Check "refuses a stale plan id" { $stale.code -eq "stale_plan" }

  $applied = Api "POST" "/api/apply" @{ planId = $plan.staged.id; confirm = "APPLY" }
  Check "applies the plan" { $null -eq $applied.staged }
  Check "block still checksums after apply" { $applied.dump.checksum.ok -eq $true }
  $after = $applied.catalog.groups[0].params[0]
  Check "edited parameter round-trips" { $after.value -eq (-not [bool]$before.value) }
  $market = ($applied.catalog.groups | Where-Object { $_.id -eq "identity" }).params | Where-Object { $_.id -eq "marketCode" } | Select-Object -First 1
  Check "second edit round-trips" { $market.display -eq "US" }

  Check "apply exposes an undo step" { $applied.capabilities.undo -eq $true -and $applied.capabilities.undoDepth -eq 1 }
  $undo = Api "POST" "/api/undo" @{}
  Check "undo restores the pre-write block" { $undo.dump.hex -eq $read.dump.hex }
  Check "undo empties the undo history" { $undo.capabilities.undo -eq $false -and $undo.capabilities.undoDepth -eq 0 }
  $noUndo = ApiError "POST" "/api/undo" @{}
  Check "refuses undo with no history" { $noUndo.code -eq "nothing_to_undo" }

  $stagedAgain = Api "POST" "/api/plan" @{ edits = @(@{ id = "drlMode"; value = 3 }) }
  Check "stages a plan for the catalog-change test" { $null -ne $stagedAgain.staged }
  # ReadAllText keeps this a plain string; Get-Content -Raw decorates it with
  # provider note properties that ConvertTo-Json would try to walk.
  $catalogText = [System.IO.File]::ReadAllText((Join-Path $root "catalog\p3.demo.json"))
  $reimport = Api "POST" "/api/catalog/import" @{ text = $catalogText }
  Check "catalog import drops the staged plan" { $null -eq $reimport.staged }
  Check "catalog import keeps a same-length block for re-decoding" { $reimport.dump.length -eq 254 }

  Write-Host "`nValidation" -ForegroundColor Cyan
  $unknown = ApiError "POST" "/api/plan" @{ edits = @(@{ id = "nope"; value = 1 }) }
  Check "rejects an unknown parameter" { $unknown.code -eq "invalid_data" }

  $outOfRange = ApiError "POST" "/api/plan" @{ edits = @(@{ id = "speedLimitWarn"; value = 9000 }) }
  Check "rejects an out-of-range value" { $outOfRange.code -eq "invalid_data" }

  $badEnum = ApiError "POST" "/api/plan" @{ edits = @(@{ id = "drlMode"; value = 9 }) }
  Check "rejects an unlisted enum value" { $badEnum.code -eq "invalid_data" }

  $short = ApiError "POST" "/api/dump/import" @{ hex = "00 11 22 33" }
  Check "rejects a block of the wrong length" { $short.code -eq "bad_length" }

  $hex = ($read.dump.hex -replace " ", "")
  $imported = Api "POST" "/api/dump/import" @{ hex = $hex }
  Check "imports a 254-byte hex block" { $imported.dump.length -eq 254 -and $imported.dump.source -eq "imported" }
  Check "imported block keeps its checksum verdict" { $imported.dump.checksum.ok -eq $true }

  $badEdits = ApiError "POST" "/api/plan" @{ edits = @{ id = "drlMode"; value = 2 } }
  Check "rejects edits that are not an array" { $badEdits.code -eq "bad_edits" }

  $corruptBytes = $read.dump.hex.Split(' ')
  $corruptBytes[2] = ([Convert]::ToInt32($corruptBytes[2], 16) -bxor 0xFF).ToString('X2')
  $corrupt = Api "POST" "/api/dump/import" @{ hex = ($corruptBytes -join ' ') }
  Check "flags an imported block with a bad checksum" { $corrupt.dump.checksum.ok -eq $false }
  Check "warns about the corrupt block" { ($corrupt.warnings -join " ") -match "fails its own checksum" }
  $corruptPlan = ApiError "POST" "/api/plan" @{ edits = @(@{ id = "drlMode"; value = 2 }) }
  Check "refuses to stage from a corrupt block" { $corruptPlan.code -eq "invalid_data" -and $corruptPlan.message -match "checksum" }

  Write-Host "`nHTTP hardening" -ForegroundColor Cyan
  $originCode = 200
  try {
    Invoke-WebRequest -Method POST -Uri ($base + "/api/discard") -Headers @{ Origin = "http://evil.example" } `
      -ContentType "application/json" -Body "{}" -UseBasicParsing | Out-Null
  } catch {
    $originCode = [int]$_.Exception.Response.StatusCode
  }
  Check "rejects a foreign-origin state change" { $originCode -eq 403 }

  $localOriginCode = 0
  try {
    Invoke-WebRequest -Method POST -Uri ($base + "/api/discard") -Headers @{ Origin = "http://localhost:5173" } `
      -ContentType "application/json" -Body "{}" -UseBasicParsing | Out-Null
    $localOriginCode = 200
  } catch {
    $localOriginCode = [int]$_.Exception.Response.StatusCode
  }
  Check "accepts a local-origin state change" { $localOriginCode -eq 200 }

  if (Get-Command curl.exe -ErrorAction SilentlyContinue) {
    $traversal = & curl.exe -s -o NUL -w "%{http_code}" --path-as-is ($base + "/%2e%2e%2fREADME.md")
    Check "refuses encoded path traversal" { $traversal.Trim() -eq "404" }
  }

  Write-Host "`nHardware mode" -ForegroundColor Cyan
  $hardware = Api "POST" "/api/mode" @{ mode = "hardware" }
  Check "switches to hardware mode" { $hardware.mode -eq "hardware" }
  Check "profile advertises no read path" { $hardware.capabilities.read -eq $false -and $hardware.capabilities.reason -ne $null }
  $noRead = ApiError "POST" "/api/read" @{}
  Check "refuses to invent a config read" { $noRead.code -eq "no_read_path" }
  $hardwareUndo = ApiError "POST" "/api/undo" @{}
  Check "refuses simulator undo in hardware mode" { $hardwareUndo.code -eq "writes_disabled" }

  Write-Host "`nCatalog" -ForegroundColor Cyan
  $badCatalog = ApiError "POST" "/api/catalog/import" @{ text = '{"id":"test.bad","configLength":254,"checksum":{"enabled":true,"byteIndex":1,"from":2,"to":254},"groups":[{"id":"g","label":"G","params":[{"id":"a","label":"A","type":"bool","offset":1,"bit":0}]}]}' }
  Check "rejects a catalogue that puts a parameter on the checksum byte" { $badCatalog.code -eq "bad_catalog" }

  $unknownType = ApiError "POST" "/api/catalog/import" @{ text = '{"id":"test.type","configLength":8,"groups":[{"id":"g","label":"G","params":[{"id":"a","label":"A","type":"u32","offset":0}]}]}' }
  Check "rejects an unknown parameter type" { $unknownType.code -eq "bad_catalog" -and $unknownType.message -match "unsupported" }

  $wideMax = ApiError "POST" "/api/catalog/import" @{ text = '{"id":"test.scale","configLength":8,"groups":[{"id":"g","label":"G","params":[{"id":"a","label":"A","type":"u8","offset":0,"max":300,"scale":1}]}]}' }
  Check "rejects a value that cannot be stored at its scale" { $wideMax.code -eq "bad_catalog" -and $wideMax.message -match "cannot be stored" }
} catch {
  $failures++
  Write-Host ("`nERROR: " + $_.Exception.Message) -ForegroundColor Red
} finally {
  if ($process -and -not $process.HasExited) { Stop-Process -Id $process.Id -Force }
}

if ($failures -gt 0) {
  Write-Host "`n$failures check(s) failed." -ForegroundColor Red
  exit 1
}
Write-Host "`nAll smoke checks passed." -ForegroundColor Green
exit 0
