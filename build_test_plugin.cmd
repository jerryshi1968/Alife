@echo off
echo Building TestPlugin...
dotnet build "%~dp0TestPlugin\TestPlugin.csproj" -c Release
if %errorlevel% neq 0 (
    echo [Error] Build failed!
    pause
    exit /b
)
echo Copying dll to Plugins folder...
if not exist "C:\Users\13309\OneDrive\Alife.Storage\Plugins" mkdir "C:\Users\13309\OneDrive\Alife.Storage\Plugins"
copy /Y "%~dp0Outputs\TestPlugin\TestPlugin.dll" "C:\Users\13309\OneDrive\Alife.Storage\Plugins\"
echo.
echo [Success] TestPlugin updated!
pause
