# Builds ResMan.exe and runs the logic tests, using the C# compiler that ships with Windows.
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc (.NET Framework 4.x is required)." }

New-Item -ItemType Directory -Force -Path build | Out-Null
$common = @('-nologo', '-codepage:65001', '-warn:4', '-warnaserror+', '-optimize+',
            '-r:System.dll', '-r:System.Xml.dll', '-r:System.Xml.Linq.dll', '-r:System.Core.dll')

& $csc @common '-out:build\LogicTests.exe' 'tests\LogicTests.cs' 'src\AutoSwitch.cs' 'src\Settings.cs'
if ($LASTEXITCODE -ne 0) { throw 'Test build failed.' }
& '.\build\LogicTests.exe'
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

$app = @('-target:winexe', '-out:build\ResMan.exe', '-win32manifest:app.manifest',
         '-r:System.Windows.Forms.dll', '-r:System.Drawing.dll')
if (Test-Path 'assets\resman.ico') { $app += '-win32icon:assets\resman.ico' }
& $csc @common @app 'src\*.cs'
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
Write-Host "Built $(Resolve-Path build\ResMan.exe)"
