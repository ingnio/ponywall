@echo off
REM Publishes both TinyWall.Avalonia and TinyWallService as self-contained
REM single-file Win64 executables to publish\.
REM
REM Run from the repo root in any shell. The output goes to publish\
REM with both binaries and any required runtime files side by side.

setlocal
set OUTDIR=%~dp0publish

if exist "%OUTDIR%" rmdir /s /q "%OUTDIR%"
mkdir "%OUTDIR%"

echo.
echo === Publishing TinyWallService ===
dotnet publish TinyWallService\TinyWallService.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=embedded ^
    -o "%OUTDIR%"
if errorlevel 1 goto fail

echo.
echo === Publishing TinyWall.Avalonia ===
dotnet publish TinyWall.Avalonia\TinyWall.Avalonia.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:EnableCompressionInSingleFile=true ^
    -p:DebugType=embedded ^
    -o "%OUTDIR%"
if errorlevel 1 goto fail

echo.
echo === Publish complete ===
echo Output: %OUTDIR%
dir "%OUTDIR%\*.exe"
goto end

:fail
echo.
echo === PUBLISH FAILED ===
exit /b 1

:end
endlocal
