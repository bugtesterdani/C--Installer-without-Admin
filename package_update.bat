@echo off
setlocal

if "%~1"=="" (
    echo Verwendung: %~nx0 ^<Version^> [Runtime] [Configuration]
    echo z.B.:     %~nx0 1.0.7 win-x64 Release
    exit /b 1
)

set VERSION=%~1
set RUNTIME=%~2
if "%RUNTIME%"=="" set RUNTIME=win-x64
set CONFIG=%~3
if "%CONFIG%"=="" set CONFIG=Release

set REPO_ROOT=%~dp0
set PUBLISH_DIR=%REPO_ROOT%artifacts\update\%VERSION%\payload
set ZIP_PATH=%REPO_ROOT%artifacts\update\%VERSION%\meineapp_%VERSION%.zip
set MANIFEST_SCRIPT=%REPO_ROOT%pythonsecret\create_manifest.py
set PRIVATE_KEY=%REPO_ROOT%pythonsecret\private.pem
set UPDATE_BASE_URL=%UPDATE_BASE_URL%
if "%UPDATE_BASE_URL%"=="" set UPDATE_BASE_URL=http://localhost:8000

echo.
echo === 1/4: Bereinige Ausgabepfade ===  REM Saubere Startbasis
if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"
if not exist "%PUBLISH_DIR%" mkdir "%PUBLISH_DIR%"
if exist "%ZIP_PATH%" del "%ZIP_PATH%"

echo.
echo === 2/4: Baue Anwendung (%CONFIG%, %RUNTIME%) ===  REM Publisht die App ins Payload-Verzeichnis
dotnet publish "%REPO_ROOT%MeineApp\MeineApp.csproj" -c %CONFIG% -r %RUNTIME% --self-contained false -o "%PUBLISH_DIR%"
if errorlevel 1 (
    echo [ERROR] dotnet publish fehlgeschlagen.
    exit /b %errorlevel%
)

echo.
echo === 3/4: Signiere Manifest ===  REM SHA-256-Hashes + RSA-Signatur erzeugen
python "%MANIFEST_SCRIPT%" --payload-dir "%PUBLISH_DIR%" --version %VERSION% --private-key "%PRIVATE_KEY%" --output "%PUBLISH_DIR%\manifest.json"
if errorlevel 1 (
    echo [ERROR] Manifest konnte nicht erzeugt werden (Python/Dependencies?).
    exit /b %errorlevel%
)

echo.
echo === 4/4: Packe ZIP und update.json ===  REM Payload + Metadaten für den Update-Server
powershell -Command "Compress-Archive -Path '%PUBLISH_DIR%\*' -DestinationPath '%ZIP_PATH%' -Force"
if errorlevel 1 (
    echo [ERROR] Compress-Archive fehlgeschlagen.
    exit /b %errorlevel%
)

set UPDATE_JSON=%REPO_ROOT%artifacts\update\%VERSION%\update.json
powershell -Command "Set-Content -Path '%UPDATE_JSON%' -Value (@{ Version = '%VERSION%'; Url = '%UPDATE_BASE_URL%/meineapp_%VERSION%.zip' } | ConvertTo-Json)"

echo.
echo Fertig! Dateien:
echo   Payload: %PUBLISH_DIR%
echo   ZIP:     %ZIP_PATH%
echo   update.json: %UPDATE_JSON%
echo Uebertrage ZIP und update.json auf den Update-Server (z.B. pythonserver).

exit /b 0
