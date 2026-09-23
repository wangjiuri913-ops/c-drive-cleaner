param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not(Test-Path -LiteralPath $compiler)){$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
if(-not(Test-Path -LiteralPath $compiler)){throw '.NET Framework C# compiler was not found.'}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$output=Join-Path (Resolve-Path $OutputDirectory) 'CDriveCleaner.exe'
$argsList=@('/nologo','/target:winexe','/optimize+','/codepage:65001','/reference:System.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll',('/win32manifest:'+(Join-Path $PSScriptRoot 'CDriveCleaner.manifest')),('/out:'+$output),(Join-Path $PSScriptRoot 'CDriveCleaner.cs'))
& $compiler @argsList
if($LASTEXITCODE -ne 0){throw 'Build failed'}
Write-Output $output
