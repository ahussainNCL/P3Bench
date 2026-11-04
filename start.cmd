@echo off
setlocal
cd /d "%~dp0"

if not exist "bridge\P3OrbitBridge.exe" (
  call bridge\build.cmd
  if errorlevel 1 exit /b 1
)

if not exist "web\dist\index.html" (
  pushd web
  call npm install
  if errorlevel 1 (popd & exit /b 1)
  call npm run build
  if errorlevel 1 (popd & exit /b 1)
  popd
)

start "" http://127.0.0.1:8787/
bridge\P3OrbitBridge.exe --port 8787
