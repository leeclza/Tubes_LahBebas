@echo off
set MODE=dev
if "%MODE%"=="dev" (
    rmdir /s /q bin obj >nul 2>&1
    dotnet build >nul
    dotnet run --no-build >nul
)