@echo off
setlocal

REM Path to the Doxygen configuration
set DOXYFILE=%~dp0Doxyfile
set DOCS_DIR=%~dp0docs

if not exist "%DOXYFILE%" (
    echo [ERROR] Doxyfile nicht gefunden unter %DOXYFILE%
    exit /b 1
)

echo === Schritt 1/2: Doxygen erzeugt HTML und LaTeX ===
doxygen "%DOXYFILE%"
if errorlevel 1 (
    echo [ERROR] Doxygen-Lauf fehlgeschlagen. Ist doxygen installiert und im PATH?
    exit /b %errorlevel%
)

echo.
echo === Schritt 2/2: PDF aus LaTeX bauen (falls LaTeX-Toolchain vorhanden) ===
set LATEX_DIR=%DOCS_DIR%\latex
set PDF=%LATEX_DIR%\refman.pdf

if exist "%LATEX_DIR%" (
    pushd "%LATEX_DIR%"
    if exist make.bat (
        call make.bat
    ) else (
        REM Fallback: direkter pdflatex-Aufruf (benötigt TeX Live/MiKTeX)
        pdflatex -interaction=nonstopmode refman.tex
        pdflatex -interaction=nonstopmode refman.tex
    )
    popd
    if exist "%PDF%" (
        echo PDF erzeugt: %PDF%
    ) else (
        echo [WARN] refman.pdf wurde nicht erzeugt. Prüfe LaTeX-Installation.
    )
) else (
    echo [WARN] LaTeX-Ausgabeordner nicht gefunden. Wurde Schritt 1 erfolgreich abgeschlossen?
)

echo Fertig. HTML liegt unter: %DOCS_DIR%\html
exit /b 0
