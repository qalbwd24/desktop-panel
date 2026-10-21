$ErrorActionPreference = 'Stop'
$testRoot = Join-Path $PSScriptRoot ('.verification\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
$executable = Join-Path $PSScriptRoot 'bin\DesktopPanel.exe'
function Invoke-Check([string]$directory, [string[]]$options) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    $arguments = @('--data-dir',('"' + $directory + '"')) + $options
    $process = Start-Process -FilePath $executable -ArgumentList $arguments -WorkingDirectory $PSScriptRoot -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(45000)) { Stop-Process -Id $process.Id; throw '验证超时；已停止本次验证进程。' }
    if ($process.ExitCode -ne 0) { throw ('验证进程失败：' + $process.ExitCode) }
    $errorPath = Join-Path $directory 'smoke-error.txt'
    if (Test-Path -LiteralPath $errorPath) { throw (Get-Content -LiteralPath $errorPath -Encoding UTF8 -Raw) }
}
Invoke-Check (Join-Path $testRoot 'store') @('--selftest')
$startupResult = Get-Content -LiteralPath (Join-Path $testRoot 'store\startup-test-result.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if (!$startupResult.passed) { throw '开机自启开关验证失败。' }
$surfaceRoot = Join-Path $testRoot 'desktop'
Invoke-Check $surfaceRoot @('--smoke')
$first = Get-Content -LiteralPath (Join-Path $surfaceRoot 'smoke-result.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if (!$first.frontend.passed -or !$first.desktopAttached -or !$first.outsideRegion -or !$first.visible -or !$first.desktopDialogs) { throw '首次桌面验证失败。' }
Copy-Item -LiteralPath (Join-Path $surfaceRoot 'smoke-result.json') -Destination (Join-Path $surfaceRoot 'first-run.json')
Invoke-Check $surfaceRoot @('--smoke')
$second = Get-Content -LiteralPath (Join-Path $surfaceRoot 'smoke-result.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if (!$second.frontend.passed -or !$second.frontend.restart -or !$second.desktopDialogs) { throw '重启后数据/样式或桌面输入验证失败。' }
$apiRoot = Join-Path $testRoot 'api-desktop'
Invoke-Check $apiRoot @('--smoke','--api-port','18881')
$apiResult = Get-Content -LiteralPath (Join-Path $apiRoot 'smoke-result.json') -Encoding UTF8 -Raw | ConvertFrom-Json
if (!$apiResult.frontend.passed -or !$apiResult.frontend.apiSync) { throw '后台 API 与组件实时同步验证失败。' }
$summary = @{ passed=$true; directory=$testRoot; store=$true; startup=$startupResult.passed; desktopAttached=$second.desktopAttached; desktopDialogs=$second.desktopDialogs; restart=$second.frontend.restart; outsideRegion=$second.outsideRegion; monitors=$second.monitorCount; apiSync=$apiResult.frontend.apiSync; linkedCountdown=$second.frontend.linkedCountdown; integerFormat=$second.frontend.integerFormat; unitSaved=$second.frontend.unitSaved; appearance=$second.frontend.styleSaved }
$summary | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $testRoot 'result.json') -Encoding UTF8
$summary | ConvertTo-Json
