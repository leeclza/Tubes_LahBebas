@echo off
REM TemplateBot.cmd - Run the bot in development or release mode
REM Set MODE=dev for development (default, always rebuilds)
REM Set MODE=release for release (only runs if bin exists)

set MODE=dev

if "%MODE%"=="dev" (
    rmdir /s /q bin obj >nul 2>&1
    dotnet build
    dotnet run --no-build
) else if "%MODE%"=="release" (
    if exist bin\ (
        dotnet run --no-build
    ) else (
        dotnet build
        dotnet run --no-build
    )
) else (
    echo Error: Invalid MODE value. Use "dev" or "release".
)



