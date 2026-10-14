@echo off
rem Builds the GliderSuit DLL and deploys the mod to the game's Mods folder.
rem Double-click to run. Stops before deploying if the build fails.
setlocal
cd /d "%~dp0"

echo === Building GliderSuit ===
dotnet build "src\GliderSuit\GliderSuit.csproj" -c Release -nologo
if errorlevel 1 (
    echo.
    echo BUILD FAILED. Nothing was deployed.
    goto end
)

echo.
echo === Deploying to the game ===
powershell -NoProfile -ExecutionPolicy Bypass -File "tools\deploy.ps1"
if errorlevel 1 (
    echo.
    echo DEPLOY FAILED.
    goto end
)

echo.
echo All done. Launch the game without EAC.

:end
echo.
pause
