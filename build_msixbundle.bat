@echo off
setlocal enabledelayedexpansion

REM Optional: override defaults (can be set via parameter 1)
set CONFIG=Release
set PLATFORMS=x86|x64|ARM64
if not "%~1"=="" set CONFIG=%~1

set REPO_ROOT=%~dp0
set OUT_DIR=%REPO_ROOT%artifacts\msix\

if not exist "%OUT_DIR%" mkdir "%OUT_DIR%"

REM Locate MSBuild if not provided
if "%MSBUILD_EXE%"=="" (
    set VSWHERE="%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
    if exist %VSWHERE% (
        for /f "usebackq tokens=*" %%i in (`%VSWHERE% -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\\**\\Bin\\MSBuild.exe`) do (
            set MSBUILD_EXE=%%i
        )
    )
)

if "%MSBUILD_EXE%"=="" (
    echo [ERROR] MSBuild.exe nicht gefunden. Setze MSBUILD_EXE oder installiere die MSBuild-Komponente von Visual Studio.
    exit /b 1
)

set SIGNING_PROPS=
if defined SIGNING_PFX set SIGNING_PROPS=/p:PackageCertificateKeyFile="%SIGNING_PFX%"
if defined SIGNING_PWD set SIGNING_PROPS=%SIGNING_PROPS% /p:PackageCertificatePassword=%SIGNING_PWD%

echo Baue MSIX-Bundle mit %MSBUILD_EXE% ...
"%MSBUILD_EXE%" "MSIX Installer\MSIX Installer.wapproj" ^
  /p:Configuration=%CONFIG% ^
  /p:AppxBundle=Always ^
  /p:AppxBundlePlatforms="%PLATFORMS%" ^
  /p:UapAppxPackageBuildMode=StoreUpload ^
  /p:AppxPackageDir=%OUT_DIR% ^
  /p:GenerateAppInstallerFile=false ^
  %SIGNING_PROPS%

if errorlevel 1 (
    echo [ERROR] MSIX-Bundle konnte nicht erstellt werden.
    exit /b %errorlevel%
)

echo MSIX-Bundle abgelegt unter: %OUT_DIR%
exit /b 0
