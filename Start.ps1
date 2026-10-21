param([switch]$Preview)
$ErrorActionPreference = 'Stop'
$executable = Join-Path $PSScriptRoot 'bin\DesktopPanel.exe'
if (!(Test-Path -LiteralPath $executable)) { & (Join-Path $PSScriptRoot 'build.ps1') }
$options = @()
if ($Preview) { $options += '--preview' }
if ($options.Count) { Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -ArgumentList $options -WindowStyle Hidden }
else { Start-Process -FilePath $executable -WorkingDirectory $PSScriptRoot -WindowStyle Hidden }
