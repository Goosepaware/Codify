@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0notify-toggle.ps1" enable
pause
