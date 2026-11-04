@echo off
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found. Install .NET Framework 4.x.
  exit /b 1
)

"%CSC%" /nologo /target:exe /platform:x86 /optimize+ /out:P3OrbitBridge.exe ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Web.Extensions.dll ^
  Program.cs App.cs Http.cs J2534.cs IsoTp.cs Catalog.cs Simulator.cs Plan.cs Profile.cs

if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo Built P3OrbitBridge.exe
