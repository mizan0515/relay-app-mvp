@echo off
rem Autopilot admin dashboard launcher.
rem Double-click to open the Korean HTML dashboard.
rem
rem IMPORTANT: this file MUST be saved as CP949 (Korean ANSI) with CRLF
rem line endings. NEVER save as UTF-8 and NEVER save with LF-only endings.
rem Either one will break cmd.exe parsing of the Korean script filename.

cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File ".\관리자.ps1" dashboard

if errorlevel 1 (
  echo.
  echo [!] 대시보드 열기에 실패했어요. 위 에러 메시지를 개발자에게 보여주세요.
  echo [!] Dashboard launch failed. Please show the error above to a developer.
  pause
)
