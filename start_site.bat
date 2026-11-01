@echo off
cd /d "%~dp0AmbulanceSite"
where dotnet >nul 2>nul || (echo .NET SDK not found. Install .NET 8 SDK from https://dotnet.microsoft.com/download & pause & exit /b)
start "" cmd /c "timeout /t 10 >nul & start http://localhost:5080"
dotnet run
pause
