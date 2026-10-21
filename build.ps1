param([switch]$Download)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$dependencyRoot = Join-Path $projectRoot '.dependencies\webview2'
$sdkVersion = '1.0.4258.31'
if ($Download -or !(Test-Path -LiteralPath (Join-Path $dependencyRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    New-Item -ItemType Directory -Path (Join-Path $projectRoot '.dependencies') -Force | Out-Null
    $request = @{ Uri = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg"; OutFile = (Join-Path $projectRoot '.dependencies\webview2.zip'); UseBasicParsing = $true }
    $proxy = Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    if ($proxy.ProxyEnable -eq 1 -and $proxy.ProxyServer -match '^127\.0\.0\.1:\d+$') { $request.Proxy = 'http://' + $proxy.ProxyServer }
    Invoke-WebRequest @request
    Expand-Archive -LiteralPath $request.OutFile -DestinationPath $dependencyRoot -Force
    Set-Content -LiteralPath (Join-Path $projectRoot '.dependencies\webview2-version.txt') -Value $sdkVersion -Encoding UTF8
}
$outputRoot = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows .NET Framework 4.8 的 C# 编译器。' }
$referenceRoot = Join-Path $dependencyRoot 'lib\net462'
$compileArgs = @('/nologo','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $outputRoot 'DesktopPanel.exe')),('/win32manifest:' + (Join-Path $projectRoot 'host\app.manifest')),'/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll',('/r:' + (Join-Path $referenceRoot 'Microsoft.Web.WebView2.Core.dll')),('/r:' + (Join-Path $referenceRoot 'Microsoft.Web.WebView2.WinForms.dll')))
$compileArgs += '/r:Microsoft.CSharp.dll' # Windows 内置 WSH COM 的动态调用，无新增下载依赖。
$compileArgs += @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'host') -Filter '*.cs' | ForEach-Object { $_.FullName })
$iconPath = Join-Path $projectRoot 'assets\app.ico'
$compileArgs += @(('/win32icon:' + $iconPath),('/resource:' + $iconPath + ',DesktopPanel.AppIcon'))
& $compiler @compileArgs
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath (Join-Path $referenceRoot 'Microsoft.Web.WebView2.Core.dll'),(Join-Path $referenceRoot 'Microsoft.Web.WebView2.WinForms.dll'),(Join-Path $dependencyRoot 'build\native\x64\WebView2Loader.dll') -Destination $outputRoot -Force
@'
<?xml version="1.0" encoding="utf-8"?>
<configuration><startup useLegacyV2RuntimeActivationPolicy="true"><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8" /></startup></configuration>
'@ | Set-Content -LiteralPath (Join-Path $outputRoot 'DesktopPanel.exe.config') -Encoding UTF8
Write-Output ('构建完成：' + (Join-Path $outputRoot 'DesktopPanel.exe'))
