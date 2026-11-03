$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe'
$outputDirectory = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$sourcePath = Join-Path $PSScriptRoot 'Bacheca.cs'
$exePath = Join-Path $outputDirectory 'Bacheca.exe'
& $compiler /nologo /target:winexe /platform:x86 /optimize+ "/out:$exePath" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Security.dll $sourcePath
if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita' }
Copy-Item -LiteralPath "$PSScriptRoot/Bacheca.exe.config" -Destination $outputDirectory
Copy-Item -LiteralPath "$PSScriptRoot/LEGGIMI.txt" -Destination $outputDirectory
Write-Output "Player compilato: $outputDirectory/Bacheca.exe"
