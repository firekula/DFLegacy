@echo off
setlocal
"%~dp0DFLegacy.Manager.exe" --root "%~dp0." %*
if errorlevel 1 pause
