@echo off
if not exist "%~dp0Builds\Windows\GoapPlayground.exe" (
  echo Open this project in Unity and choose Tools - GOAP Playground - Build Windows.
  pause
  exit /b 1
)
start "" "%~dp0Builds\Windows\GoapPlayground.exe" -screen-fullscreen 0 -screen-width 1280 -screen-height 800
