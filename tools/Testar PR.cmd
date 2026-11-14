@echo off
rem Duplo clique: baixa e abre o .exe de teste de um PR (FLUXO-01 PR 2).
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0testar-pr.ps1"
if errorlevel 1 pause
