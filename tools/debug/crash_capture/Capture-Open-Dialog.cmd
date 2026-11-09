@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Capture-DNF.ps1" -Mode snapshot
pause
