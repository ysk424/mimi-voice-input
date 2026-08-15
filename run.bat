@echo off
setlocal
set "APP=%~dp0bin\Release\Mimi.exe"

if not exist "%APP%" (
  echo mimi を初回ビルドしています...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" -Configuration Release
  if errorlevel 1 (
    echo.
    echo ビルドに失敗しました。
    pause
    exit /b 1
  )
)

start "" "%APP%"
