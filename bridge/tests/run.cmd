@echo off
setlocal
cd /d "%~dp0"

set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo csc.exe not found. Install .NET Framework 4.x.
  exit /b 1
)

"%CSC%" /nologo /target:exe /platform:x86 /optimize+ /out:P3OrbitBridge.Tests.exe ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Web.Extensions.dll ^
  Tests.cs ..\Catalog.cs ..\Plan.cs ..\IsoTp.cs ..\Simulator.cs ..\Profile.cs

if errorlevel 1 (
  echo Test build failed.
  exit /b 1
)

P3OrbitBridge.Tests.exe
exit /b %errorlevel%
