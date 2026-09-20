@echo off
set "GoapEditor=C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe"
if not exist "%GoapEditor%" (
  echo Unity 6000.6.0f1 was not found. Add this folder as a project in Unity Hub.
  pause
  exit /b 1
)
start "" "%GoapEditor%" -projectPath "%~dp0."
